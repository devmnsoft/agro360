using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agro360.Application;
using Agro360.Application.Abstractions;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Infrastructure.Security;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Agro360.Infrastructure.Services;

public sealed record MyDayItem(
    Guid Id,
    string Source,
    string Title,
    DateTimeOffset? Deadline,
    string Priority,
    string SuggestedAction,
    string Link,
    string Category,
    string? UnitName = null);

public sealed record MyDayResult(
    IReadOnlyList<MyDayItem> Items,
    IReadOnlyList<string> SourcesConsulted,
    IReadOnlyList<string> UnavailableSources,
    DateTimeOffset Timestamp,
    int TotalCount);

public interface IMyDayService
{
    Task<MyDayResult> GetMyDayAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MyDayItem>> GetPendingTasksAsync(CancellationToken ct = default);
}

public sealed class MyDayService(
    DatabaseExecutor db,
    ITenantContext tenant,
    ILogger<MyDayService> logger) : IMyDayService
{
    public async Task<IReadOnlyList<MyDayItem>> GetPendingTasksAsync(CancellationToken ct = default)
    {
        var result = await GetMyDayAsync(ct).ConfigureAwait(false);
        return result.Items;
    }

    public async Task<MyDayResult> GetMyDayAsync(CancellationToken ct = default)
    {
        if (!tenant.IsAvailable || tenant.TenantId == Guid.Empty || tenant.UserId == Guid.Empty)
        {
            return new MyDayResult(
                Items: Array.Empty<MyDayItem>(),
                SourcesConsulted: Array.Empty<string>(),
                UnavailableSources: Array.Empty<string>(),
                Timestamp: DateTimeOffset.UtcNow,
                TotalCount: 0);
        }

        var items = new List<MyDayItem>();
        var consulted = new List<string>();
        var unavailable = new List<string>();

        // 1. Obter permissões efetivas, contratos ativos e escopo de unidade do usuário
        HashSet<string> permissions = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> contractedModules = new(StringComparer.OrdinalIgnoreCase);
        bool isPlatformAdmin = false;
        bool isTenantAdmin = false;
        bool hasAllScope = false;

        try
        {
            await db.InTenantTransactionAsync(async (conn, tx) =>
            {
                var perms = await conn.QueryAsync<string>(new CommandDefinition(
                    """
                    select distinct p.code
                    from agro360.identity_users u
                    join agro360.identity_user_roles ur on ur.tenant_id=u.tenant_id and ur.user_id=u.id
                    join agro360.identity_role_permissions rp on rp.tenant_id=ur.tenant_id and rp.role_id=ur.role_id
                    join agro360.identity_permissions p on p.id=rp.permission_id
                    where u.tenant_id=@TenantId and u.id=@UserId and u.status='ACTIVE' and u.deleted_at is null
                    """,
                    new { tenant.TenantId, tenant.UserId },
                    tx,
                    cancellationToken: ct)).ConfigureAwait(false);

                foreach (var p in perms) permissions.Add(p);

                var modules = await conn.QueryAsync<string>(new CommandDefinition(
                    $"""
                    select module_code from ({EntitlementQueries.ModuleCodeSelect}) em
                    """,
                    new { tenant.TenantId },
                    tx,
                    cancellationToken: ct)).ConfigureAwait(false);

                foreach (var m in modules) contractedModules.Add(m);

                isPlatformAdmin = permissions.Contains("platform.admin");
                isTenantAdmin = permissions.Contains("saas.admin") || permissions.Contains("account.manage");

                hasAllScope = isPlatformAdmin || isTenantAdmin || await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                    """
                    select exists(
                        select 1 from agro360.identity_user_unit_scopes
                        where tenant_id=@TenantId and user_id=@UserId and scope_type='ALL'
                    )
                    """,
                    new { tenant.TenantId, tenant.UserId },
                    tx,
                    cancellationToken: ct)).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            InfrastructureLogMessages.MyDaySourceFailed(logger, "Identity.Entitlements", tenant.TenantId, ex);
            unavailable.Add("Identity.Entitlements");
            return new MyDayResult(
                Items: Array.Empty<MyDayItem>(),
                SourcesConsulted: consulted,
                UnavailableSources: unavailable,
                Timestamp: DateTimeOffset.UtcNow,
                TotalCount: 0);
        }

        // 2. Compras: Requisições aguardando aprovação
        if (contractedModules.Contains("procurement") &&
            (isTenantAdmin || permissions.Contains("procurement.approve") || permissions.Contains("procurement.manage") || permissions.Contains("procurement.view")))
        {
            const string sourceName = "Procurement.Requisitions";
            consulted.Add(sourceName);
            try
            {
                await db.InTenantTransactionAsync(async (conn, tx) =>
                {
                    var reqs = await conn.QueryAsync<dynamic>(new CommandDefinition(
                        """
                        select r.id, r.number, r.needed_on, r.priority, f.name as farm_name
                        from agro360.procurement_requisitions r
                        left join agro360.geo_farms f on f.tenant_id = r.tenant_id and f.id = r.property_id
                        where r.tenant_id = @TenantId
                          and r.status = 'AWAITING_APPROVAL'
                          and r.deleted_at is null
                          and (@HasAllScope or r.property_id is null or exists (
                              select 1 from agro360.identity_user_unit_scopes s
                              where s.tenant_id = @TenantId and s.user_id = @UserId
                                and ((s.scope_type = 'FARM' and s.farm_id = r.property_id) or
                                     (s.scope_type = 'ORGANIZATION' and s.organization_id = f.organization_id))
                          ))
                        order by r.created_at desc
                        limit 20
                        """,
                        new { tenant.TenantId, tenant.UserId, HasAllScope = hasAllScope },
                        tx,
                        cancellationToken: ct)).ConfigureAwait(false);

                    foreach (var r in reqs)
                    {
                        DateTimeOffset? deadline = r.needed_on is DateOnly d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)) : null;
                        string priority = ((string?)r.priority)?.ToUpperInvariant() is "URGENT" or "HIGH" ? "HIGH" : "MEDIUM";
                        items.Add(new MyDayItem(
                            Id: (Guid)r.id,
                            Source: "Procurement",
                            Title: $"Requisição {r.number} aguardando aprovação",
                            Deadline: deadline,
                            Priority: priority,
                            SuggestedAction: "Analisar Requisição",
                            Link: "/Procurement#requisitions",
                            Category: "APPROVAL",
                            UnitName: (string?)r.farm_name));
                    }
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InfrastructureLogMessages.MyDaySourceFailed(logger, sourceName, tenant.TenantId, ex);
                unavailable.Add(sourceName);
            }
        }

        // 3. Recebimento e Qualidade: Itens em Quarentena / Inspeção Pendente
        if (contractedModules.Contains("procurement") &&
            (isTenantAdmin || permissions.Contains("procurement.receive") || permissions.Contains("procurement.quality") || permissions.Contains("quality.manage")))
        {
            const string sourceName = "Procurement.Quarantine";
            consulted.Add(sourceName);
            try
            {
                await db.InTenantTransactionAsync(async (conn, tx) =>
                {
                    var quarantine = await conn.QueryAsync<dynamic>(new CommandDefinition(
                        """
                        select q.receipt_item_id as id, r.id as receipt_id, r.number as rec_num, q.created_at
                        from agro360.procurement_receipt_quarantine q
                        join agro360.procurement_receipts r on r.tenant_id = q.tenant_id and r.id = q.receipt_id
                        where q.tenant_id = @TenantId
                          and q.status = 'PENDING'
                        order by q.created_at asc
                        limit 20
                        """,
                        new { tenant.TenantId },
                        tx,
                        cancellationToken: ct)).ConfigureAwait(false);

                    foreach (var q in quarantine)
                    {
                        DateTimeOffset? createdAt = q.created_at is DateTimeOffset dto ? dto : null;
                        items.Add(new MyDayItem(
                            Id: (Guid)q.id,
                            Source: "Quality",
                            Title: $"Item do recebimento {q.rec_num} em quarentena aguarda inspeção",
                            Deadline: createdAt?.AddDays(2),
                            Priority: "HIGH",
                            SuggestedAction: "Inspecionar Lote",
                            Link: "/Procurement#receipts",
                            Category: "QUALITY"));
                    }
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InfrastructureLogMessages.MyDaySourceFailed(logger, sourceName, tenant.TenantId, ex);
                unavailable.Add(sourceName);
            }
        }

        // 4. Operações de Campo: Ordens de Serviço sob responsabilidade ou aguardando revisão
        if (contractedModules.Contains("agriculture") &&
            (isTenantAdmin || permissions.Contains("agriculture.execute") || permissions.Contains("agriculture.view") || permissions.Contains("agriculture.manage")))
        {
            const string sourceName = "FieldOperations.WorkOrders";
            consulted.Add(sourceName);
            try
            {
                await db.InTenantTransactionAsync(async (conn, tx) =>
                {
                    var workOrders = await conn.QueryAsync<dynamic>(new CommandDefinition(
                        """
                        select r.id, r.data->>'name' as name, r.status, r.created_at,
                               (r.data->>'propertyId')::uuid as farm_id, f.name as farm_name
                        from agro360.agriculture_records r
                        left join agro360.geo_farms f on f.tenant_id = r.tenant_id and f.id = (r.data->>'propertyId')::uuid
                        where r.tenant_id = @TenantId
                          and r.module = 'work-orders'
                          and r.status in ('PLANNED', 'IN_PROGRESS', 'AWAITING_REVIEW')
                          and r.deleted_at is null
                          and (@HasAllScope or exists (
                              select 1 from agro360.identity_user_unit_scopes s
                              where s.tenant_id = @TenantId and s.user_id = @UserId
                                and ((s.scope_type = 'FARM' and s.farm_id = (r.data->>'propertyId')::uuid) or
                                     (s.scope_type = 'ORGANIZATION' and s.organization_id = f.organization_id))
                          ))
                        order by r.created_at desc
                        limit 20
                        """,
                        new { tenant.TenantId, tenant.UserId, HasAllScope = hasAllScope },
                        tx,
                        cancellationToken: ct)).ConfigureAwait(false);

                    foreach (var o in workOrders)
                    {
                        var status = (string)o.status;
                        var title = status switch
                        {
                            "AWAITING_REVIEW" => $"Ordem de campo '{o.name}' aguarda conferência e encerramento",
                            "IN_PROGRESS" => $"Ordem de campo '{o.name}' em execução",
                            _ => $"Ordem de campo '{o.name}' planejada para início"
                        };
                        var priority = status == "AWAITING_REVIEW" ? "HIGH" : "MEDIUM";
                        var action = status == "AWAITING_REVIEW" ? "Revisar e Concluir" : "Apontar Operação";
                        DateTimeOffset? createdAt = o.created_at is DateTimeOffset dto ? dto : null;

                        items.Add(new MyDayItem(
                            Id: (Guid)o.id,
                            Source: "FieldOperations",
                            Title: title,
                            Deadline: createdAt?.AddDays(3),
                            Priority: priority,
                            SuggestedAction: action,
                            Link: "/Field",
                            Category: "OPERATION",
                            UnitName: (string?)o.farm_name));
                    }
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InfrastructureLogMessages.MyDaySourceFailed(logger, sourceName, tenant.TenantId, ex);
                unavailable.Add(sourceName);
            }
        }

        // 5. Financeiro: Títulos vencidos a pagar (SOMENTE com permissão e contrato financeiro)
        if (contractedModules.Contains("finance") &&
            (isTenantAdmin || permissions.Contains("finance.view") || permissions.Contains("finance.payables") || permissions.Contains("finance.manage")))
        {
            const string sourceName = "Finance.Payables";
            consulted.Add(sourceName);
            try
            {
                await db.InTenantTransactionAsync(async (conn, tx) =>
                {
                    var overduePayables = await conn.QueryAsync<dynamic>(new CommandDefinition(
                        """
                        select p.id, p.document, p.due_on, p.final_amount, f.name as farm_name
                        from agro360.finance_payables p
                        left join agro360.geo_farms f on f.tenant_id = p.tenant_id and f.id = p.farm_id
                        where p.tenant_id = @TenantId
                          and p.status in ('OPEN', 'OVERDUE')
                          and p.due_on < current_date
                          and p.deleted_at is null
                        order by p.due_on asc
                        limit 20
                        """,
                        new { tenant.TenantId },
                        tx,
                        cancellationToken: ct)).ConfigureAwait(false);

                    foreach (var f in overduePayables)
                    {
                        DateTimeOffset? deadline = f.due_on is DateOnly d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)) : null;
                        decimal amount = (decimal)f.final_amount;

                        items.Add(new MyDayItem(
                            Id: (Guid)f.id,
                            Source: "Finance",
                            Title: $"Título a pagar '{f.document}' vencido (R$ {amount:N2})",
                            Deadline: deadline,
                            Priority: "CRITICAL",
                            SuggestedAction: "Regularizar Pagamento",
                            Link: "/Finance#payables",
                            Category: "FINANCE",
                            UnitName: (string?)f.farm_name));
                    }
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InfrastructureLogMessages.MyDaySourceFailed(logger, sourceName, tenant.TenantId, ex);
                unavailable.Add(sourceName);
            }
        }

        // 6. Conciliação: Divergências de 3-Way Matching de Faturas
        if (contractedModules.Contains("procurement") &&
            (isTenantAdmin || permissions.Contains("procurement.manage") || permissions.Contains("procurement.view") || permissions.Contains("finance.view")))
        {
            const string sourceName = "Procurement.MatchDivergences";
            consulted.Add(sourceName);
            try
            {
                await db.InTenantTransactionAsync(async (conn, tx) =>
                {
                    var matchDivergences = await conn.QueryAsync<dynamic>(new CommandDefinition(
                        """
                        select d.id, d.invoice_match_id, d.type, d.description, d.created_at, doc.document_number, s.legal_name as supplier_name
                        from agro360.procurement_match_divergences d
                        join agro360.procurement_invoice_matches m on m.tenant_id = d.tenant_id and m.id = d.invoice_match_id
                        join agro360.procurement_billing_documents doc on doc.tenant_id = m.tenant_id and doc.id = m.billing_document_id
                        join agro360.procurement_suppliers s on s.tenant_id = doc.tenant_id and s.id = doc.supplier_id
                        where d.tenant_id = @TenantId
                          and d.status = 'OPEN'
                          and d.deleted_at is null
                        order by d.created_at desc
                        limit 20
                        """,
                        new { tenant.TenantId },
                        tx,
                        cancellationToken: ct)).ConfigureAwait(false);

                    foreach (var d in matchDivergences)
                    {
                        DateTimeOffset? createdAt = d.created_at is DateTimeOffset dto ? dto : null;
                        items.Add(new MyDayItem(
                            Id: (Guid)d.id,
                            Source: "Matching",
                            Title: $"Divergência de fatura na NF {d.document_number} ({d.supplier_name}) - {d.type}",
                            Deadline: createdAt?.AddDays(2),
                            Priority: "HIGH",
                            SuggestedAction: "Resolver Divergência",
                            Link: "/Procurement#matches",
                            Category: "CONCILIATION"));
                    }
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InfrastructureLogMessages.MyDaySourceFailed(logger, sourceName, tenant.TenantId, ex);
                unavailable.Add(sourceName);
            }
        }

        // 7. Frota: Manutenções Preventivas e Corretivas Vencidas / Próximas
        if ((contractedModules.Contains("operations") || contractedModules.Contains("fleet")) &&
            (isTenantAdmin || permissions.Contains("maintenance.read") || permissions.Contains("fleet.read") || permissions.Contains("operations.view")))
        {
            const string sourceName = "Fleet.Maintenance";
            consulted.Add(sourceName);
            try
            {
                await db.InTenantTransactionAsync(async (conn, tx) =>
                {
                    var fleetOrders = await conn.QueryAsync<dynamic>(new CommandDefinition(
                        """
                        select m.id, m.type, m.description, coalesce(m.scheduled_for, m.next_review_date) as due_date, a.identification as asset_name, f.name as farm_name
                        from agro360.fleet_maintenance_orders m
                        join agro360.fleet_assets a on a.tenant_id = m.tenant_id and a.id = m.asset_id
                        left join agro360.geo_farms f on f.tenant_id = a.tenant_id and f.id = a.farm_id
                        where m.tenant_id = @TenantId
                          and m.status not in ('COMPLETED', 'CANCELLED')
                          and coalesce(m.scheduled_for, m.next_review_date) <= (current_date + 7)
                        order by due_date asc
                        limit 20
                        """,
                        new { tenant.TenantId },
                        tx,
                        cancellationToken: ct)).ConfigureAwait(false);

                    foreach (var m in fleetOrders)
                    {
                        DateTimeOffset? deadline = m.due_date is DateOnly d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)) : null;
                        var isOverdue = deadline.HasValue && deadline.Value.Date < DateTime.UtcNow.Date;
                        var priority = isOverdue ? "CRITICAL" : "HIGH";

                        items.Add(new MyDayItem(
                            Id: (Guid)m.id,
                            Source: "Fleet",
                            Title: $"Manutenção {m.type} do ativo '{m.asset_name}': {m.description}",
                            Deadline: deadline,
                            Priority: priority,
                            SuggestedAction: "Executar Manutenção",
                            Link: "/Fleet",
                            Category: "FLEET",
                            UnitName: (string?)m.farm_name));
                    }
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InfrastructureLogMessages.MyDaySourceFailed(logger, sourceName, tenant.TenantId, ex);
                unavailable.Add(sourceName);
            }
        }

        // 8. Qualidade: Incidentes e Não Conformidades Abertas
        if (contractedModules.Contains("procurement") &&
            (isTenantAdmin || permissions.Contains("quality.manage") || permissions.Contains("quality.view") || permissions.Contains("procurement.quality")))
        {
            const string sourceName = "Procurement.QualityIncidents";
            consulted.Add(sourceName);
            try
            {
                await db.InTenantTransactionAsync(async (conn, tx) =>
                {
                    var incidents = await conn.QueryAsync<dynamic>(new CommandDefinition(
                        """
                        select q.id, q.incident_type, q.severity, q.description, q.created_at, s.legal_name as supplier_name, p.name as product_name
                        from agro360.procurement_quality_incidents q
                        join agro360.procurement_suppliers s on s.tenant_id = q.tenant_id and s.id = q.supplier_id
                        join agro360.inventory_products p on p.tenant_id = q.tenant_id and p.id = q.product_id
                        where q.tenant_id = @TenantId
                          and q.status = 'OPEN'
                          and q.deleted_at is null
                        order by q.created_at desc
                        limit 20
                        """,
                        new { tenant.TenantId },
                        tx,
                        cancellationToken: ct)).ConfigureAwait(false);

                    foreach (var inc in incidents)
                    {
                        DateTimeOffset? createdAt = inc.created_at is DateTimeOffset dto ? dto : null;
                        string severity = ((string?)inc.severity)?.ToUpperInvariant() ?? "HIGH";
                        string priority = severity is "CRITICAL" ? "CRITICAL" : "HIGH";

                        items.Add(new MyDayItem(
                            Id: (Guid)inc.id,
                            Source: "Quality",
                            Title: $"Incidente de Qualidade ({inc.incident_type}) no insumo '{inc.product_name}' - {inc.supplier_name}",
                            Deadline: createdAt?.AddDays(1),
                            Priority: priority,
                            SuggestedAction: "Tratar Não Conformidade",
                            Link: "/Inspections",
                            Category: "QUALITY"));
                    }
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InfrastructureLogMessages.MyDaySourceFailed(logger, sourceName, tenant.TenantId, ex);
                unavailable.Add(sourceName);
            }
        }

        var sortedItems = items
            .OrderByDescending(x => x.Priority == "CRITICAL")
            .ThenByDescending(x => x.Priority == "HIGH")
            .ThenBy(x => x.Deadline ?? DateTimeOffset.MaxValue)
            .ToList();

        return new MyDayResult(
            Items: sortedItems,
            SourcesConsulted: consulted,
            UnavailableSources: unavailable,
            Timestamp: DateTimeOffset.UtcNow,
            TotalCount: sortedItems.Count);
    }
}
