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

/// <summary>
/// Painel "Meu dia": agrega pendências reais de cada módulo. As fontes só são consultadas quando o
/// módulo está no contrato efetivo do tenant E o usuário possui a permissão canônica correspondente
/// (Permissions.ModulesForPermission é a única fonte do vínculo permissão→módulo). A isolamento de
/// unidade usa sempre a política canônica OperationalScopePolicy sobre tabelas que carregam
/// property_id; fontes sem dimensão de unidade (financeiro, não conformidades) são declaradas como
/// escopo tenant e nunca inventam unidade nem prazo.
/// </summary>
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

        // 1. Permissões efetivas e módulos contratados (dimensões distintas de unidade).
        HashSet<string> permissions = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> contractedModules = new(StringComparer.OrdinalIgnoreCase);

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
                    new { tenant.TenantId, tenant.UserId, tenant.FarmId },
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

        // Gate canônico: permissão só vale se o módulo contratado corresponder (mesmo mapa das policies HTTP).
        bool Can(string permission) =>
            permissions.Contains(permission) && Permissions.ModulesForPermission(permission).Any(contractored => contractedModules.Contains(contractored));

        await RunSourceAsync("Procurement.PendingRequisitions", consulted, unavailable, Can(Permissions.PurchasingApprove), async () =>
        {
            await db.InTenantTransactionAsync(async (conn, tx) =>
            {
                var reqs = await conn.QueryAsync<dynamic>(new CommandDefinition(
                    $"""
                    select r.id, r.number, r.needed_on, r.priority, f.name as farm_name
                    from agro360.procurement_requisitions r
                    left join agro360.geo_farms f on f.tenant_id=r.tenant_id and f.id=r.property_id and f.deleted_at is null
                    where r.tenant_id=@TenantId and r.status='AWAITING_APPROVAL' and r.deleted_at is null
                      {OperationalScopePolicy.Sql("r")}
                    order by r.created_at desc
                    limit 20
                    """,
                    new { tenant.TenantId, tenant.UserId, tenant.FarmId },
                    tx,
                    cancellationToken: ct)).ConfigureAwait(false);

                foreach (var r in reqs)
                {
                    items.Add(new MyDayItem(
                        Id: (Guid)r.id,
                        Source: "Procurement",
                        Title: $"Requisição {r.number} aguardando aprovação",
                        Deadline: ToDeadline(r.needed_on),
                        Priority: ((string?)r.priority)?.ToUpperInvariant() is "URGENT" or "HIGH" ? "HIGH" : "MEDIUM",
                        SuggestedAction: "Analisar Requisição",
                        Link: "/Procurement#requisitions",
                        Category: "APPROVAL",
                        UnitName: (string?)r.farm_name));
                }
            }, ct).ConfigureAwait(false);
        });

        await RunSourceAsync("Procurement.Quarantine", consulted, unavailable, Can(Permissions.PurchasingReceive), async () =>
        {
            // Quarentena pertence ao recebimento de um pedido; o escopo de unidade vem do pedido (property_id).
            await db.InTenantTransactionAsync(async (conn, tx) =>
            {
                var quarantine = await conn.QueryAsync<dynamic>(new CommandDefinition(
                    $"""
                    select q.receipt_item_id as id, r.number as rec_num, po.number as order_num,
                           s.legal_name as supplier_name, p.name as product_name
                    from agro360.procurement_receipt_quarantine q
                    join agro360.procurement_receipt_items ri on ri.tenant_id=q.tenant_id and ri.id=q.receipt_item_id and ri.deleted_at is null
                    join agro360.procurement_receipts r on r.tenant_id=ri.tenant_id and r.id=ri.receipt_id and r.deleted_at is null
                    join agro360.procurement_purchase_orders po on po.tenant_id=r.tenant_id and po.id=r.purchase_order_id and po.deleted_at is null
                    left join agro360.procurement_suppliers s on s.tenant_id=po.tenant_id and s.id=po.supplier_id
                    left join agro360.inventory_products p on p.tenant_id=q.tenant_id and p.id=q.product_id and p.deleted_at is null
                    left join agro360.geo_farms f on f.tenant_id=po.tenant_id and f.id=po.property_id and f.deleted_at is null
                    where q.tenant_id=@TenantId and q.status='PENDING'
                      {OperationalScopePolicy.Sql("po")}
                    order by q.created_at asc
                    limit 20
                    """,
                    new { tenant.TenantId, tenant.UserId, tenant.FarmId },
                    tx,
                    cancellationToken: ct)).ConfigureAwait(false);

                foreach (var q in quarantine)
                {
                    items.Add(new MyDayItem(
                        Id: (Guid)q.id,
                        Source: "Quality",
                        Title: $"Item '{q.product_name}' do recebimento {q.rec_num} (pedido {q.order_num}, fornecedor {q.supplier_name}) em quarentena aguarda inspeção",
                        Deadline: null, // Sem prazo de inspeção registrado na quarentena — não inventar.
                        Priority: "HIGH",
                        SuggestedAction: "Inspecionar Lote",
                        Link: "/Procurement#receipts",
                        Category: "QUALITY"));
                }
            }, ct).ConfigureAwait(false);
        });

        await RunSourceAsync("FieldOperations.WorkOrders", consulted, unavailable, Can(Permissions.AgricultureRead), async () =>
        {
            await db.InTenantTransactionAsync(async (conn, tx) =>
            {
                var workOrders = await conn.QueryAsync<dynamic>(new CommandDefinition(
                    $"""
                    select r.id, r.data->>'name' as name, r.status, f.name as farm_name
                    from agro360.agriculture_records r
                    left join agro360.geo_farms f on f.tenant_id=r.tenant_id and f.id=(r.data->>'propertyId')::uuid and f.deleted_at is null
                    where r.tenant_id=@TenantId and r.module='work-orders'
                      and r.status in ('PLANNED', 'IN_PROGRESS', 'AWAITING_REVIEW')
                      and r.deleted_at is null
                      {OperationalScopePolicy.FarmSql("f")}
                    order by r.updated_at desc
                    limit 20
                    """,
                    new { tenant.TenantId, tenant.UserId, tenant.FarmId },
                    tx,
                    cancellationToken: ct)).ConfigureAwait(false);

                foreach (var o in workOrders)
                {
                    var status = (string)o.status;
                    items.Add(new MyDayItem(
                        Id: (Guid)o.id,
                        Source: "FieldOperations",
                        Title: status switch
                        {
                            "AWAITING_REVIEW" => $"Ordem de campo '{o.name}' aguarda conferência e encerramento",
                            "IN_PROGRESS" => $"Ordem de campo '{o.name}' em execução",
                            _ => $"Ordem de campo '{o.name}' planejada para início"
                        },
                        Deadline: null, // A ordem de campo não registra prazo próprio — não inventar data.
                        Priority: status == "AWAITING_REVIEW" ? "HIGH" : "MEDIUM",
                        SuggestedAction: status == "AWAITING_REVIEW" ? "Revisar e Concluir" : "Apontar Operação",
                        Link: "/Field",
                        Category: "OPERATION",
                        UnitName: (string?)o.farm_name));
                }
            }, ct).ConfigureAwait(false);
        });

        await RunSourceAsync("Finance.Payables", consulted, unavailable, Can(Permissions.FinanceRead), async () =>
        {
            await db.InTenantTransactionAsync(async (conn, tx) =>
            {
                // Títulos a pagar não possuem dimensão de unidade (são do tenant) — unidade exibida como indisponível.
                var overduePayables = await conn.QueryAsync<dynamic>(new CommandDefinition(
                    """
                    select p.id, p.document, p.due_on, p.balance
                    from agro360.finance_payables p
                    where p.tenant_id=@TenantId and p.status in ('OPEN', 'PARTIAL')
                      and p.balance > 0 and p.due_on < current_date
                    order by p.due_on asc
                    limit 20
                    """,
                    new { tenant.TenantId, tenant.UserId, tenant.FarmId },
                    tx,
                    cancellationToken: ct)).ConfigureAwait(false);

                foreach (var payable in overduePayables)
                {
                    decimal amount = (decimal)payable.balance;
                    items.Add(new MyDayItem(
                        Id: (Guid)payable.id,
                        Source: "Finance",
                        Title: $"Título a pagar '{payable.document}' vencido (saldo R$ {amount:N2})",
                        Deadline: ToDeadline(payable.due_on),
                        Priority: "CRITICAL",
                        SuggestedAction: "Regularizar Pagamento",
                        Link: "/Finance#payables",
                        Category: "FINANCE"));
                }
            }, ct).ConfigureAwait(false);
        });

        await RunSourceAsync("Procurement.MatchDivergences", consulted, unavailable, Can(Permissions.PurchasingRead), async () =>
        {
            await db.InTenantTransactionAsync(async (conn, tx) =>
            {
                var matchDivergences = await conn.QueryAsync<dynamic>(new CommandDefinition(
                    $"""
                    select d.id, d.type, d.description, d.due_at, doc.document_number,
                           s.legal_name as supplier_name, f.name as farm_name
                    from agro360.procurement_match_divergences d
                    join agro360.procurement_invoice_matches m on m.tenant_id=d.tenant_id and m.id=d.invoice_match_id and m.deleted_at is null
                    join agro360.procurement_billing_documents doc on doc.tenant_id=m.tenant_id and doc.id=m.billing_document_id and doc.deleted_at is null
                    join agro360.procurement_purchase_orders po on po.tenant_id=m.tenant_id and po.id=m.purchase_order_id and po.deleted_at is null
                    join agro360.procurement_suppliers s on s.tenant_id=doc.tenant_id and s.id=doc.supplier_id
                    left join agro360.geo_farms f on f.tenant_id=po.tenant_id and f.id=po.property_id and f.deleted_at is null
                    where d.tenant_id=@TenantId and d.status='OPEN' and d.deleted_at is null
                      {OperationalScopePolicy.Sql("po")}
                    order by d.due_at asc nulls last, d.created_at desc
                    limit 20
                    """,
                    new { tenant.TenantId, tenant.UserId, tenant.FarmId },
                    tx,
                    cancellationToken: ct)).ConfigureAwait(false);

                foreach (var d in matchDivergences)
                {
                    items.Add(new MyDayItem(
                        Id: (Guid)d.id,
                        Source: "Matching",
                        Title: $"Divergência de fatura na NF {d.document_number} ({d.supplier_name}) - {d.type}: {d.description}",
                        Deadline: d.due_at is DateTimeOffset dueAt ? new DateTimeOffset(dueAt.UtcDateTime) : null,
                        Priority: "HIGH",
                        SuggestedAction: "Resolver Divergência",
                        Link: "/Procurement#matches",
                        Category: "CONCILIATION",
                        UnitName: (string?)d.farm_name));
                }
            }, ct).ConfigureAwait(false);
        });

        await RunSourceAsync("Fleet.Maintenance", consulted, unavailable, Can(Permissions.FleetRead), async () =>
        {
            await db.InTenantTransactionAsync(async (conn, tx) =>
            {
                var fleetOrders = await conn.QueryAsync<dynamic>(new CommandDefinition(
                    $"""
                    select m.id, m.type, m.description, coalesce(m.scheduled_for, m.next_review_date) as due_date,
                           a.identification as asset_name, f.name as farm_name
                    from agro360.fleet_maintenance_orders m
                    join agro360.fleet_assets a on a.tenant_id=m.tenant_id and a.id=m.asset_id and a.deleted_at is null
                    left join agro360.geo_farms f on f.tenant_id=a.tenant_id and f.id=a.property_id and f.deleted_at is null
                    where m.tenant_id=@TenantId and m.deleted_at is null
                      and m.status not in ('COMPLETED', 'CANCELLED')
                      and coalesce(m.scheduled_for, m.next_review_date) <= (current_date + 7)
                      {OperationalScopePolicy.Sql("a")}
                    order by due_date asc
                    limit 20
                    """,
                    new { tenant.TenantId, tenant.UserId, tenant.FarmId },
                    tx,
                    cancellationToken: ct)).ConfigureAwait(false);

                foreach (var m in fleetOrders)
                {
                    var deadline = ToDeadline(m.due_date);
                    items.Add(new MyDayItem(
                        Id: (Guid)m.id,
                        Source: "Fleet",
                        Title: $"Manutenção {m.type} do ativo '{m.asset_name}': {m.description}",
                        Deadline: deadline,
                        Priority: deadline.HasValue && deadline.Value.Date < DateTimeOffset.UtcNow.Date ? "CRITICAL" : "HIGH",
                        SuggestedAction: "Executar Manutenção",
                        Link: "/Fleet",
                        Category: "FLEET",
                        UnitName: (string?)m.farm_name));
                }
            }, ct).ConfigureAwait(false);
        });

        await RunSourceAsync("Quality.NonConformities", consulted, unavailable, Can(Permissions.ComplianceRead), async () =>
        {
            await db.InTenantTransactionAsync(async (conn, tx) =>
            {
                // Tabela canônica de NCs dos módulos Compliance/Inspections (donos em src). Não carrega
                // vínculo provável de unidade (unit_id sem FK) — escopo do tenant, como nos serviços donos;
                // unidade exibida como indisponível por natureza. Vocabulário aberto = não encerradas/canceladas.
                var nonConformities = await conn.QueryAsync<dynamic>(new CommandDefinition(
                    """
                    select n.id, n.number, n.title, n.severity, n.due_on
                    from agro360.compliance_non_conformities n
                    where n.tenant_id=@TenantId and n.deleted_at is null
                      and n.status not in ('CLOSED','CANCELLED')
                      and n.due_on is not null and n.due_on <= current_date + 7
                    order by n.due_on asc
                    limit 20
                    """,
                    new { tenant.TenantId, tenant.UserId, tenant.FarmId },
                    tx,
                    cancellationToken: ct)).ConfigureAwait(false);

                foreach (var n in nonConformities)
                {
                    DateOnly dueOn = (DateOnly)n.due_on;
                    bool overdue = dueOn <= DateOnly.FromDateTime(DateTime.UtcNow);
                    items.Add(new MyDayItem(
                        Id: (Guid)n.id,
                        Source: "Quality",
                        Title: $"Não conformidade #{n.number}: {n.title}",
                        Deadline: new DateTimeOffset(dueOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
                        Priority: overdue || ((string?)n.severity)?.ToUpperInvariant() is "CRITICAL"
                            ? "CRITICAL"
                            : ((string?)n.severity)?.ToUpperInvariant() == "HIGH" ? "HIGH" : "MEDIUM",
                        SuggestedAction: "Tratar Não Conformidade",
                        Link: "/Inspections",
                        Category: "QUALITY"));
                }
            }, ct).ConfigureAwait(false);
        });

        await RunSourceAsync("Procurement.QuotationDecisions", consulted, unavailable, Can(Permissions.PurchasingApprove), async () =>
        {
            // Cotações com propostas recebidas aguardam decisão de compra; o escopo de unidade vem da requisição de origem.
            await db.InTenantTransactionAsync(async (conn, tx) =>
            {
                var quotations = await conn.QueryAsync<dynamic>(new CommandDefinition(
                    $"""
                    select q.id, q.number, q.valid_until, req.number as req_number, f.name as farm_name
                    from agro360.procurement_quotations q
                    left join agro360.procurement_requisitions req on req.tenant_id=q.tenant_id and req.id=q.requisition_id and req.deleted_at is null
                    left join agro360.geo_farms f on f.tenant_id=q.tenant_id and f.id=req.property_id and f.deleted_at is null
                    where q.tenant_id=@TenantId and q.status in ('RESPONDED','ANALYSIS') and q.deleted_at is null
                      {OperationalScopePolicy.Sql("req")}
                    order by q.valid_until asc nulls last, q.created_at desc
                    limit 20
                    """,
                    new { tenant.TenantId, tenant.UserId, tenant.FarmId },
                    tx,
                    cancellationToken: ct)).ConfigureAwait(false);

                foreach (var q in quotations)
                {
                    var origin = string.IsNullOrWhiteSpace((string?)q.req_number) ? string.Empty : $" (requisição {q.req_number})";
                    DateOnly? validUntil = q.valid_until is DateOnly vu ? vu : null;
                    items.Add(new MyDayItem(
                        Id: (Guid)q.id,
                        Source: "Procurement",
                        Title: $"Cotação {q.number}{origin} com propostas aguardando decisão de compra",
                        Deadline: ToDeadline(q.valid_until),
                        // Proposta vencida ou vencendo em 3 dias sobe a prioridade; sem prazo registrado não inventa urgência.
                        Priority: validUntil is not null && validUntil.Value <= DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3) ? "HIGH" : "MEDIUM",
                        SuggestedAction: "Decidir Cotação",
                        Link: "/Procurement#quotations",
                        Category: "APPROVAL",
                        UnitName: (string?)q.farm_name));
                }
            }, ct).ConfigureAwait(false);
        });

        await RunSourceAsync("Procurement.PurchaseOrderApprovals", consulted, unavailable, Can(Permissions.PurchasingApprove), async () =>
        {
            await db.InTenantTransactionAsync(async (conn, tx) =>
            {
                var orders = await conn.QueryAsync<dynamic>(new CommandDefinition(
                    $"""
                    select po.id, po.number, po.delivery_on, s.legal_name as supplier_name, f.name as farm_name
                    from agro360.procurement_purchase_orders po
                    join agro360.procurement_suppliers s on s.tenant_id=po.tenant_id and s.id=po.supplier_id
                    left join agro360.geo_farms f on f.tenant_id=po.tenant_id and f.id=po.property_id and f.deleted_at is null
                    where po.tenant_id=@TenantId and po.status='AWAITING_APPROVAL' and po.deleted_at is null
                      {OperationalScopePolicy.Sql("po")}
                    order by po.created_at asc
                    limit 20
                    """,
                    new { tenant.TenantId, tenant.UserId, tenant.FarmId },
                    tx,
                    cancellationToken: ct)).ConfigureAwait(false);

                foreach (var po in orders)
                {
                    items.Add(new MyDayItem(
                        Id: (Guid)po.id,
                        Source: "Procurement",
                        Title: $"Pedido de compra {po.number} ({po.supplier_name}) aguardando aprovação",
                        Deadline: null, // delivery_on é prazo de entrega, não de aprovação — não inventar prazo.
                        Priority: "HIGH",
                        SuggestedAction: "Aprovar Pedido",
                        Link: "/Procurement#orders",
                        Category: "APPROVAL",
                        UnitName: (string?)po.farm_name));
                }
            }, ct).ConfigureAwait(false);
        });

        await RunSourceAsync("Inventory.MaterialRequests", consulted, unavailable, Can(Permissions.InventoryAdjust), async () =>
        {
            await db.InTenantTransactionAsync(async (conn, tx) =>
            {
                var requests = await conn.QueryAsync<dynamic>(new CommandDefinition(
                    $"""
                    select r.id, 'REQ-'||lpad(r.number::text,6,'0') as number, r.needed_on, r.priority, f.name as farm_name
                    from agro360.inventory_material_requests r
                    join agro360.geo_farms f on f.tenant_id=r.tenant_id and f.id=r.farm_id and f.deleted_at is null
                    where r.tenant_id=@TenantId and r.status='AWAITING_APPROVAL'
                      {OperationalScopePolicy.FarmSql("f")}
                    order by r.needed_on asc
                    limit 20
                    """,
                    new { tenant.TenantId, tenant.UserId, tenant.FarmId },
                    tx,
                    cancellationToken: ct)).ConfigureAwait(false);

                foreach (var r in requests)
                {
                    items.Add(new MyDayItem(
                        Id: (Guid)r.id,
                        Source: "Inventory",
                        Title: $"Solicitação de material {r.number} aguardando aprovação",
                        Deadline: ToDeadline(r.needed_on),
                        Priority: ((string?)r.priority)?.ToUpperInvariant() is "URGENT" or "HIGH" ? "HIGH" : "MEDIUM",
                        SuggestedAction: "Analisar Solicitação",
                        Link: "/Inventory#requests",
                        Category: "APPROVAL",
                        UnitName: (string?)r.farm_name));
                }
            }, ct).ConfigureAwait(false);
        });

        await RunSourceAsync("Harvest.PlannedPendings", consulted, unavailable, Can(Permissions.AgricultureRead), async () =>
        {
            // Colheitas planejadas que ainda não iniciaram e chegam à janela de execução (14 dias).
            await db.InTenantTransactionAsync(async (conn, tx) =>
            {
                var plans = await conn.QueryAsync<dynamic>(new CommandDefinition(
                    $"""
                    select h.id, h.planned_end, p.name as product_name, fl.name as field_name, f.name as farm_name
                    from agro360.harvest_plans h
                    join agro360.geo_farms f on f.tenant_id=h.tenant_id and f.id=h.farm_id and f.deleted_at is null
                    left join agro360.geo_fields fl on fl.tenant_id=h.tenant_id and fl.id=h.field_id and fl.deleted_at is null
                    left join agro360.inventory_products p on p.tenant_id=h.tenant_id and p.id=h.product_id and p.deleted_at is null
                    where h.tenant_id=@TenantId and h.status='PLANNED' and h.planned_end <= current_date + 14
                      {OperationalScopePolicy.FarmSql("f")}
                    order by h.planned_end asc
                    limit 20
                    """,
                    new { tenant.TenantId, tenant.UserId, tenant.FarmId },
                    tx,
                    cancellationToken: ct)).ConfigureAwait(false);

                foreach (var h in plans)
                {
                    var deadline = ToDeadline(h.planned_end);
                    var location = string.IsNullOrWhiteSpace((string?)h.field_name) ? (string?)h.farm_name : $"{h.field_name} · {h.farm_name}";
                    items.Add(new MyDayItem(
                        Id: (Guid)h.id,
                        Source: "Harvest",
                        Title: $"Colheita planejada de '{h.product_name}' em {location} ainda sem início",
                        Deadline: deadline,
                        Priority: deadline.HasValue && deadline.Value.Date < DateTimeOffset.UtcNow.Date ? "HIGH" : "MEDIUM",
                        SuggestedAction: "Organizar Colheita",
                        Link: "/Harvest",
                        Category: "OPERATION",
                        UnitName: (string?)h.farm_name));
                }
            }, ct).ConfigureAwait(false);
        });

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

    private static DateTimeOffset? ToDeadline(object? value) => value switch
    {
        DateOnly d => new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
        DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
        DateTimeOffset dto => dto.ToUniversalTime(),
        _ => null
    };

    private async Task RunSourceAsync(string sourceName, List<string> consulted, List<string> unavailable, bool enabled, Func<Task> action)
    {
        if (!enabled) return;
        try
        {
            await action().ConfigureAwait(false);
            consulted.Add(sourceName);
        }
        catch (Exception ex)
        {
            InfrastructureLogMessages.MyDaySourceFailed(logger, sourceName, tenant.TenantId, ex);
            unavailable.Add(sourceName);
        }
    }
}
