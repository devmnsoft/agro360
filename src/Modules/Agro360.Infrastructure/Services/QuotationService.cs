using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Infrastructure.Security;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;

namespace Agro360.Infrastructure.Services;

public interface IQuotationService
{
    Task<Guid> RequestQuotationsAsync(QuotationRequestCommand command, CancellationToken ct = default);
    Task<Guid> SubmitQuotationAsync(SubmitQuotationCommand command, CancellationToken ct = default);
    Task<IReadOnlyList<dynamic>> ListAsync(ProcurementQuery query, CancellationToken ct = default);
    Task<dynamic> DetailAsync(Guid quotationId, CancellationToken ct = default);
    Task<IReadOnlyList<dynamic>> CompareQuotationsAsync(Guid requisitionId, CancellationToken ct = default);
    Task DecideAsync(Guid quotationId, QuotationDecisionCommand command, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> ConvertToOrderAsync(Guid quotationId, CancellationToken ct = default);
}

/// <summary>
/// Cotações de suprimentos. A unidade operacional é sempre herdada da requisição de origem através
/// de OperationalScopePolicy (nunca há cotação "sem unidade" visível fora do escopo do usuário);
/// fornecedores e catálogo permanecem compartilhados do tenant. A decisão por item fica registrada
/// em procurement_quotation_decisions (com justificativa quando acima do menor valor) e a conversão
/// honra essas decisões, gerando um pedido por fornecedor vencedor vinculado às linhas aprovadas da
/// requisição via requisition_item_id, sem ultrapassar o saldo autorizado (validado em CreateOrderAsync).
/// </summary>
public sealed class QuotationService(DatabaseExecutor db, ITenantContext tenant, IProcurementService procurement) : IQuotationService
{
    private static readonly string[] OpenStatuses = ["SENT", "PARTIAL", "RESPONDED", "ANALYSIS"];
    private static readonly string[] ConvertibleStatuses = ["PARTIAL", "RESPONDED", "ANALYSIS", "APPROVED"];

    public async Task<Guid> RequestQuotationsAsync(QuotationRequestCommand command, CancellationToken ct = default)
    {
        if (command.SupplierIds.Count == 0) throw new DomainException("Informe ao menos um fornecedor participante.", "agro360.quotation.suppliers_required");
        if (command.DueDate <= DateOnly.FromDateTime(DateTime.UtcNow)) throw new DomainException("O prazo para receber propostas deve ser futuro.", "agro360.quotation.due_date_invalid");

        return await db.InTenantTransactionAsync<Guid>(async (conn, tx) =>
        {
            var requisition = await conn.QuerySingleOrDefaultAsync<RequisitionSnapshot>(new CommandDefinition(
                $"""
                select r.id Id,r.status Status,r.needed_on NeededOn,r.cost_center_id CostCenterId,r.property_id PropertyId
                from agro360.procurement_requisitions r
                where r.tenant_id=@TenantId and r.id=@Id and r.deleted_at is null {OperationalScopePolicy.Sql("r")}
                for update of r
                """,
                ScopeParams(new { Id = command.RequisitionId }), tx, cancellationToken: ct))
                ?? throw new NotFoundException("Requisição", command.RequisitionId);

            if (requisition.Status is not ("APPROVED" or "PARTIALLY_FULFILLED"))
                throw new DomainException("Somente requisições aprovadas podem solicitar cotações.", "agro360.quotation.requisition_not_approved");

            var alreadyOpen = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from agro360.procurement_quotations oq where oq.tenant_id=@TenantId and oq.requisition_id=@Id and oq.status = any(@OpenStatuses) and oq.deleted_at is null)",
                ScopeParams(new { Id = command.RequisitionId, OpenStatuses }), tx, cancellationToken: ct));
            if (alreadyOpen) throw new ConflictException("Já existe uma cotação em aberto para esta requisição.", "agro360.quotation.already_open");

            var pendingLines = (await conn.QueryAsync<RequisitionRemainingLine>(new CommandDefinition(
                """
                select ri.id RequisitionItemId,ri.catalog_item_id CatalogItemId,ri.unit Unit,c.name ItemName,
                       ri.quantity-coalesce((select sum(oi.quantity)
                           from agro360.procurement_purchase_order_items oi
                           join agro360.procurement_purchase_orders o on o.tenant_id=oi.tenant_id and o.id=oi.purchase_order_id and o.status<>'CANCELLED'
                           where oi.tenant_id=ri.tenant_id and oi.requisition_item_id=ri.id),0) RemainingQuantity
                from agro360.procurement_requisition_items ri
                join agro360.procurement_item_catalog c on c.tenant_id=ri.tenant_id and c.id=ri.catalog_item_id
                where ri.tenant_id=@TenantId and ri.requisition_id=@Id and ri.deleted_at is null
                order by ri.created_at,ri.id
                """,
                ScopeParams(new { Id = command.RequisitionId }), tx, cancellationToken: ct)))
                .Where(line => line.RemainingQuantity > 0)
                .ToList();
            if (pendingLines.Count == 0) throw new ConflictException("A requisição não possui saldo autorizado pendente para cotar.", "agro360.quotation.fulfilled");

            var quotationId = Guid.CreateVersion7();
            var sequence = await conn.ExecuteScalarAsync<long>(new CommandDefinition("select nextval('agro360.procurement_document_number_seq')", transaction: tx, cancellationToken: ct));
            var number = $"COT-{DateTime.UtcNow:yyyy}-{sequence:000000}";
            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.procurement_quotations
                    (id, tenant_id, number, requisition_id, status, valid_until, created_by, updated_by)
                values
                    (@Id, @TenantId, @Number, @RequisitionId, 'SENT', @DueDate, @UserId, @UserId)
                """,
                ScopeParams(new { Id = quotationId, Number = number, command.RequisitionId, command.DueDate }), tx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.procurement_quotation_items
                    (id, tenant_id, quotation_id, catalog_item_id, requisition_item_id, quantity, unit, created_by, updated_by)
                values
                    (@Id, @TenantId, @QuotationId, @CatalogItemId, @RequisitionItemId, @Quantity, @Unit, @UserId, @UserId)
                """,
                pendingLines.Select(line => new
                {
                    Id = Guid.CreateVersion7(),
                    tenant.TenantId,
                    QuotationId = quotationId,
                    line.CatalogItemId,
                    line.RequisitionItemId,
                    Quantity = line.RemainingQuantity,
                    line.Unit,
                    tenant.UserId
                }), tx, cancellationToken: ct));

            foreach (var supplierId in command.SupplierIds.Distinct())
            {
                // Fornecedor é cadastro compartilhado do tenant; a exigência de escopo é apenas da requisição.
                var supplierValid = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "select exists(select 1 from agro360.procurement_suppliers where tenant_id=@TenantId and id=@SupplierId and status in('ACTIVE','APPROVED') and deleted_at is null)",
                    ScopeParams(new { SupplierId = supplierId }), tx, cancellationToken: ct));
                if (!supplierValid) throw new DomainException("Fornecedor participante inexistente, inativo ou não elegível.", "agro360.quotation.supplier_invalid");
                await conn.ExecuteAsync(new CommandDefinition(
                    "insert into agro360.procurement_quotation_suppliers(id, tenant_id, quotation_id, supplier_id, status, created_by, updated_by) values(gen_random_uuid(), @TenantId, @QuotationId, @SupplierId, 'PENDING', @UserId, @UserId)",
                    ScopeParams(new { QuotationId = quotationId, SupplierId = supplierId }), tx, cancellationToken: ct));
            }

            await Audit(conn, tx, quotationId, "REQUESTED", new { command.RequisitionId, Suppliers = command.SupplierIds.Distinct().Count(), command.DueDate }, ct);
            return quotationId;
        }, ct);
    }

    public async Task<Guid> SubmitQuotationAsync(SubmitQuotationCommand command, CancellationToken ct = default)
    {
        if (command.UnitPrice <= 0 || command.DeliveryDays < 0 || command.Discount < 0)
            throw new DomainException("Preço, desconto e prazo da proposta são inválidos.", "agro360.quotation.response_invalid");
        if ((command.Freight is not null && command.Freight < 0) || (command.Taxes is not null && command.Taxes < 0))
            throw new DomainException("Frete e impostos da proposta são inválidos.", "agro360.quotation.response_invalid");

        return await db.InTenantTransactionAsync<Guid>(async (conn, tx) =>
        {
            var participant = await conn.QuerySingleOrDefaultAsync<ParticipantSnapshot>(new CommandDefinition(
                $"""
                select qs.id Id,q.status Status
                from agro360.procurement_quotation_suppliers qs
                join agro360.procurement_quotations q on q.tenant_id=qs.tenant_id and q.id=qs.quotation_id and q.deleted_at is null
                join agro360.procurement_requisitions scope_req on scope_req.tenant_id=q.tenant_id and scope_req.id=q.requisition_id and scope_req.deleted_at is null
                where qs.tenant_id=@TenantId and qs.quotation_id=@QuotationId and qs.supplier_id=@SupplierId and qs.deleted_at is null
                  {OperationalScopePolicy.Sql("scope_req")}
                for update of qs,q
                """,
                ScopeParams(new { command.QuotationId, command.SupplierId }), tx, cancellationToken: ct))
                ?? throw new DomainException("Fornecedor não participa desta cotação.", "agro360.quotation.supplier_not_participant");
            if (!OpenStatuses.Contains(participant.Status)) throw new ConflictException("A cotação não aceita novas propostas.", "agro360.quotation.closed");

            var candidates = (await conn.QueryAsync<QuoteItemRow>(new CommandDefinition(
                """
                select qi.id Id,qi.quantity Quantity,qi.requisition_item_id RequisitionItemId,c.name ItemName
                from agro360.procurement_quotation_items qi
                join agro360.procurement_item_catalog c on c.tenant_id=qi.tenant_id and c.id=qi.catalog_item_id
                where qi.tenant_id=@TenantId and qi.quotation_id=@QuotationId and qi.deleted_at is null
                  and (@QuotationItemId::uuid is null or qi.id=@QuotationItemId)
                  and (@QuotationItemId::uuid is not null or qi.catalog_item_id=@CatalogItemId)
                """,
                ScopeParams(new { command.QuotationId, command.QuotationItemId, command.CatalogItemId }), tx, cancellationToken: ct))).AsList();
            if (candidates.Count == 0) throw new DomainException("Item não pertence à cotação.", "agro360.quotation.item_invalid");
            if (candidates.Count > 1) throw new DomainException("A requisição possui mais de uma linha para este item; informe a linha exata da cotação.", "agro360.quotation.item_ambiguous");
            var item = candidates[0];

            var subtotal = decimal.Round(item.Quantity * command.UnitPrice, 2, MidpointRounding.AwayFromZero);
            if (command.Discount > subtotal) throw new DomainException("O desconto não pode exceder o subtotal do item.", "agro360.quotation.discount_exceeds_subtotal");
            if (command.ProposalValidUntil is not null && command.ProposalValidUntil <= DateOnly.FromDateTime(DateTime.UtcNow))
                throw new DomainException("A validade da proposta deve ser futura.", "agro360.quotation.proposal_valid_invalid");
            var total = decimal.Round(subtotal - command.Discount, 2, MidpointRounding.AwayFromZero);

            // Reenvio substitui a proposta anterior do mesmo fornecedor para o mesmo item: o histórico
            // permanece disponível como resposta indisponível e a comparação só enxerga a vigente.
            await conn.ExecuteAsync(new CommandDefinition(
                "update agro360.procurement_quotation_responses set available=false,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and quotation_supplier_id=@QuotationSupplierId and quotation_item_id=@QuotationItemId and available and deleted_at is null",
                ScopeParams(new { QuotationSupplierId = participant.Id, QuotationItemId = item.Id }), tx, cancellationToken: ct));

            var responseId = Guid.CreateVersion7();
            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.procurement_quotation_responses
                    (id, tenant_id, quotation_item_id, quotation_supplier_id, unit_price, discount, total, available, notes, created_by, updated_by)
                values
                    (@Id, @TenantId, @QuotationItemId, @QuotationSupplierId, @UnitPrice, @Discount, @Total, true, @Notes, @UserId, @UserId)
                """,
                ScopeParams(new { Id = responseId, QuotationItemId = item.Id, QuotationSupplierId = participant.Id, command.UnitPrice, command.Discount, Total = total, command.Notes }), tx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(
                """
                update agro360.procurement_quotation_suppliers
                   set status='RESPONDED',delivery_days=@DeliveryDays,
                       payment_terms=coalesce(@PaymentTerms,payment_terms),
                       proposal_valid_until=coalesce(@ProposalValidUntil,proposal_valid_until),
                       freight=coalesce(@Freight,freight),
                       taxes=coalesce(@Taxes,taxes),
                       updated_at=now(),updated_by=@UserId
                 where tenant_id=@TenantId and id=@QuotationSupplierId
                """,
                ScopeParams(new { QuotationSupplierId = participant.Id, command.DeliveryDays, command.PaymentTerms, command.ProposalValidUntil, command.Freight, command.Taxes }), tx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(
                "update agro360.procurement_quotations set status='PARTIAL',updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@QuotationId and status='SENT'",
                ScopeParams(new { command.QuotationId }), tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                """
                update agro360.procurement_quotations q set status='RESPONDED',updated_at=now(),updated_by=@UserId
                 where q.tenant_id=@TenantId and q.id=@QuotationId and q.status='PARTIAL'
                   and not exists(
                       select 1
                       from agro360.procurement_quotation_suppliers p
                       where p.tenant_id=q.tenant_id and p.quotation_id=q.id and p.deleted_at is null
                         and exists(
                             select 1
                             from agro360.procurement_quotation_items i
                             where i.tenant_id=q.tenant_id and i.quotation_id=q.id and i.deleted_at is null
                               and not exists(
                                   select 1 from agro360.procurement_quotation_responses r
                                   where r.tenant_id=p.tenant_id and r.quotation_supplier_id=p.id and r.quotation_item_id=i.id
                                     and r.available and r.deleted_at is null))
                   )
                """,
                ScopeParams(new { command.QuotationId }), tx, cancellationToken: ct));

            await Audit(conn, tx, command.QuotationId, "PROPOSAL_SUBMITTED", new { command.SupplierId, Item = item.ItemName, command.UnitPrice, command.Discount, Total = total, command.DeliveryDays }, ct);
            return responseId;
        }, ct);
    }

    public async Task<IReadOnlyList<dynamic>> ListAsync(ProcurementQuery query, CancellationToken ct = default)
    {
        return await db.InTenantTransactionAsync<IReadOnlyList<dynamic>>(async (conn, tx) =>
        {
            var rows = await conn.QueryAsync(new CommandDefinition(
                $"""
                select q.id,q.number,q.status,q.valid_until,q.decision_reason,q.created_at,q.requisition_id,
                       r.number requisition_number,r.priority requisition_priority,
                       coalesce(f.name,'Não informada') operational_unit,
                       (select count(*) from agro360.procurement_quotation_suppliers ps where ps.tenant_id=q.tenant_id and ps.quotation_id=q.id and ps.deleted_at is null) supplier_count,
                       (select count(distinct pr.quotation_supplier_id) from agro360.procurement_quotation_responses pr
                          join agro360.procurement_quotation_items pi on pi.tenant_id=pr.tenant_id and pi.id=pr.quotation_item_id and pi.deleted_at is null
                          where pr.tenant_id=q.tenant_id and pi.quotation_id=q.id and pr.available and pr.deleted_at is null) responded_suppliers,
                       (select count(*) from agro360.procurement_purchase_orders po where po.tenant_id=q.tenant_id and po.quotation_id=q.id and po.status<>'CANCELLED' and po.deleted_at is null) converted_orders
                from agro360.procurement_quotations q
                join agro360.procurement_requisitions r on r.tenant_id=q.tenant_id and r.id=q.requisition_id and r.deleted_at is null
                left join agro360.geo_farms f on f.tenant_id=r.tenant_id and f.id=r.property_id and f.deleted_at is null
                where q.tenant_id=@TenantId and q.deleted_at is null
                  {OperationalScopePolicy.Sql("r")}
                  and (@Status is null or q.status=@Status)
                  and (@RequisitionId is null or q.requisition_id=@RequisitionId)
                  and (@Search is null or q.number ilike '%'||@Search||'%' or r.number ilike '%'||@Search||'%')
                order by q.created_at desc
                limit @Take offset @Skip
                """,
                ScopeParams(new
                {
                    Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search,
                    Status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status,
                    query.RequisitionId,
                    Take = Math.Clamp(query.PageSize, 1, 100),
                    Skip = (Math.Max(query.Page, 1) - 1) * Math.Clamp(query.PageSize, 1, 100)
                }), tx, cancellationToken: ct));
            return rows.AsList();
        }, ct);
    }

    public async Task<dynamic> DetailAsync(Guid quotationId, CancellationToken ct = default)
    {
        return await db.InTenantTransactionAsync<dynamic>(async (conn, tx) =>
        {
            var quotation = await conn.QuerySingleOrDefaultAsync(new CommandDefinition(
                $"""
                select q.id,q.number,q.status,q.valid_until,q.decision_reason,q.created_at,q.requisition_id,
                       r.number requisition_number,r.status requisition_status,r.needed_on requisition_needed_on,
                       coalesce(f.name,'Não informada') operational_unit
                from agro360.procurement_quotations q
                join agro360.procurement_requisitions r on r.tenant_id=q.tenant_id and r.id=q.requisition_id and r.deleted_at is null
                left join agro360.geo_farms f on f.tenant_id=r.tenant_id and f.id=r.property_id and f.deleted_at is null
                where q.tenant_id=@TenantId and q.id=@Id and q.deleted_at is null {OperationalScopePolicy.Sql("r")}
                """,
                ScopeParams(new { Id = quotationId }), tx, cancellationToken: ct))
                ?? throw new NotFoundException("Cotação", quotationId);

            var items = (await conn.QueryAsync(new CommandDefinition(
                """
                select qi.id,qi.catalog_item_id,qi.requisition_item_id,qi.quantity,qi.unit,c.name catalog_name,c.code catalog_code,
                       (select coalesce(min(r.total),0) from agro360.procurement_quotation_responses r
                         where r.tenant_id=qi.tenant_id and r.quotation_item_id=qi.id and r.available and r.deleted_at is null) lowest_total,
                       (select count(distinct r.quotation_supplier_id) from agro360.procurement_quotation_responses r
                         where r.tenant_id=qi.tenant_id and r.quotation_item_id=qi.id and r.available and r.deleted_at is null) quoted_by
                from agro360.procurement_quotation_items qi
                join agro360.procurement_item_catalog c on c.tenant_id=qi.tenant_id and c.id=qi.catalog_item_id
                where qi.tenant_id=@TenantId and qi.quotation_id=@Id and qi.deleted_at is null
                order by qi.created_at,qi.id
                """,
                new { tenant.TenantId, Id = quotationId }, tx, cancellationToken: ct))).AsList();

            var suppliers = (await conn.QueryAsync(new CommandDefinition(
                """
                select qs.id,qs.supplier_id,s.legal_name supplier_name,qs.status,qs.delivery_days,qs.payment_terms,
                       qs.proposal_valid_until,qs.freight,qs.taxes,
                       (select count(*) from agro360.procurement_quotation_responses r
                         join agro360.procurement_quotation_items i on i.tenant_id=r.tenant_id and i.id=r.quotation_item_id and i.deleted_at is null
                         where r.tenant_id=qs.tenant_id and r.quotation_supplier_id=qs.id and i.quotation_id=qs.quotation_id and r.available and r.deleted_at is null) item_count,
                       coalesce((select sum(r.total) from agro360.procurement_quotation_responses r
                         join agro360.procurement_quotation_items i on i.tenant_id=r.tenant_id and i.id=r.quotation_item_id and i.deleted_at is null
                         where r.tenant_id=qs.tenant_id and r.quotation_supplier_id=qs.id and i.quotation_id=qs.quotation_id and r.available and r.deleted_at is null),0) grand_total
                from agro360.procurement_quotation_suppliers qs
                join agro360.procurement_suppliers s on s.tenant_id=qs.tenant_id and s.id=qs.supplier_id
                where qs.tenant_id=@TenantId and qs.quotation_id=@Id and qs.deleted_at is null
                order by s.legal_name
                """,
                new { tenant.TenantId, Id = quotationId }, tx, cancellationToken: ct))).AsList();

            var responses = (await conn.QueryAsync(new CommandDefinition(
                """
                select r.id,r.quotation_item_id,qi.catalog_item_id,r.quotation_supplier_id,r.unit_price,r.discount,r.total,r.available,r.notes,r.created_at
                from agro360.procurement_quotation_responses r
                join agro360.procurement_quotation_items qi on qi.tenant_id=r.tenant_id and qi.id=r.quotation_item_id and qi.deleted_at is null
                where r.tenant_id=@TenantId and qi.quotation_id=@Id and r.deleted_at is null
                order by r.created_at desc
                """,
                new { tenant.TenantId, Id = quotationId }, tx, cancellationToken: ct))).AsList();

            var decisions = (await conn.QueryAsync(new CommandDefinition(
                """
                select d.id,d.quotation_item_id,c.name item_name,d.quotation_supplier_id,qs.supplier_id,s.legal_name supplier_name,
                       d.selected_total,d.lowest_total,d.justification,d.decided_at
                from agro360.procurement_quotation_decisions d
                join agro360.procurement_quotation_items qi on qi.tenant_id=d.tenant_id and qi.id=d.quotation_item_id
                join agro360.procurement_item_catalog c on c.tenant_id=qi.tenant_id and c.id=qi.catalog_item_id
                join agro360.procurement_quotation_suppliers qs on qs.tenant_id=d.tenant_id and qs.id=d.quotation_supplier_id
                join agro360.procurement_suppliers s on s.tenant_id=qs.tenant_id and s.id=qs.supplier_id
                where d.tenant_id=@TenantId and d.quotation_id=@Id and d.deleted_at is null
                order by qi.created_at,qi.id
                """,
                new { tenant.TenantId, Id = quotationId }, tx, cancellationToken: ct))).AsList();

            var convertedOrders = (await conn.QueryAsync(new CommandDefinition(
                """
                select o.id,o.number,o.status,o.total,o.delivery_on,o.created_at,s.legal_name supplier_name
                from agro360.procurement_purchase_orders o
                join agro360.procurement_suppliers s on s.tenant_id=o.tenant_id and s.id=o.supplier_id
                where o.tenant_id=@TenantId and o.quotation_id=@Id and o.status<>'CANCELLED' and o.deleted_at is null
                order by o.created_at
                """,
                new { tenant.TenantId, Id = quotationId }, tx, cancellationToken: ct))).AsList();

            return new { quotation, items, suppliers, responses, decisions, converted_orders = convertedOrders };
        }, ct);
    }

    public async Task<IReadOnlyList<dynamic>> CompareQuotationsAsync(Guid requisitionId, CancellationToken ct = default)
    {
        return await db.InTenantTransactionAsync<IReadOnlyList<dynamic>>(async (conn, tx) =>
        {
            var rows = await conn.QueryAsync<dynamic>(new CommandDefinition(
                $"""
                select q.id quotation_id,q.number,c.name item,coalesce(f.name,'Não informada') operational_unit,
                       s.legal_name supplier,qi.id quotation_item_id,qs.id quotation_supplier_id,
                       qi.quantity,qi.unit,qi.requisition_item_id,qr.unit_price,qr.discount,qr.total,
                       qs.delivery_days,qs.payment_terms,qs.proposal_valid_until,qs.freight,qs.taxes,qr.notes,
                       min(qr.total) over(partition by qi.id) lowest_total,
                       qr.total = min(qr.total) over(partition by qi.id) is_lowest,
                       d.quotation_supplier_id is not null decided,q.valid_until quote_deadline
                from agro360.procurement_quotations q
                join agro360.procurement_requisitions scope_req on scope_req.tenant_id=q.tenant_id and scope_req.id=q.requisition_id and scope_req.deleted_at is null
                left join agro360.geo_farms f on f.tenant_id=scope_req.tenant_id and f.id=scope_req.property_id and f.deleted_at is null
                join agro360.procurement_quotation_items qi on qi.tenant_id=q.tenant_id and qi.quotation_id=q.id and qi.deleted_at is null
                join agro360.procurement_item_catalog c on c.tenant_id=qi.tenant_id and c.id=qi.catalog_item_id
                join agro360.procurement_quotation_responses qr on qr.tenant_id=qi.tenant_id and qr.quotation_item_id=qi.id and qr.available and qr.deleted_at is null
                join agro360.procurement_quotation_suppliers qs on qs.tenant_id=qr.tenant_id and qs.id=qr.quotation_supplier_id and qs.deleted_at is null
                join agro360.procurement_suppliers s on s.tenant_id=qs.tenant_id and s.id=qs.supplier_id
                left join lateral (
                    select dd.quotation_supplier_id from agro360.procurement_quotation_decisions dd
                    where dd.tenant_id=q.tenant_id and dd.quotation_item_id=qi.id and dd.deleted_at is null
                    order by dd.decided_at desc limit 1
                ) d on true
                where q.tenant_id=@TenantId and q.requisition_id=@RequisitionId and q.deleted_at is null
                  {OperationalScopePolicy.Sql("scope_req")}
                order by qi.created_at,qi.id,qr.total asc,s.legal_name
                """,
                ScopeParams(new { RequisitionId = requisitionId }), tx, cancellationToken: ct));
            return rows.AsList();
        }, ct);
    }

    public async Task DecideAsync(Guid quotationId, QuotationDecisionCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command.Items);
        if (command.Items.Count == 0) throw new DomainException("Selecione o fornecedor de cada item da cotação.", "agro360.quotation.decision_items_required");
        if (command.Items.Select(line => line.QuotationItemId).Distinct().Count() != command.Items.Count)
            throw new DomainException("Cada item da cotação deve aparecer exatamente uma vez na decisão.", "agro360.quotation.decision_duplicate_item");

        await db.InTenantTransactionAsync(async (conn, tx) =>
        {
            var status = await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                $"select q.status from agro360.procurement_quotations q join agro360.procurement_requisitions r on r.tenant_id=q.tenant_id and r.id=q.requisition_id and r.deleted_at is null where q.tenant_id=@TenantId and q.id=@Id and q.deleted_at is null {OperationalScopePolicy.Sql("r")} for update of q",
                ScopeParams(new { Id = quotationId }), tx, cancellationToken: ct))
                ?? throw new NotFoundException("Cotação", quotationId);
            if (!ConvertibleStatuses.Append("SENT").Contains(status)) throw new ConflictException("A cotação ainda não possui propostas para decidir.", "agro360.quotation.no_proposals");
            if (status == "APPROVED")
            {
                var converted = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "select exists(select 1 from agro360.procurement_purchase_orders o where o.tenant_id=@TenantId and o.quotation_id=@Id and o.status<>'CANCELLED' and o.deleted_at is null)",
                    ScopeParams(new { Id = quotationId }), tx, cancellationToken: ct));
                if (converted) throw new ConflictException("A cotação já foi convertida em pedido; a decisão não pode mais mudar.", "agro360.quotation.already_converted");
            }

            var lines = (await conn.QueryAsync<DecisionCandidate>(new CommandDefinition(
                """
                select qi.id QuotationItemId,coalesce(min(nullif(qr.total,-1)),min(qr.total)) LowestTotal
                from agro360.procurement_quotation_items qi
                left join agro360.procurement_quotation_responses qr on qr.tenant_id=qi.tenant_id and qr.quotation_item_id=qi.id and qr.available and qr.deleted_at is null
                where qi.tenant_id=@TenantId and qi.quotation_id=@Id and qi.deleted_at is null
                group by qi.id
                """,
                ScopeParams(new { Id = quotationId }), tx, cancellationToken: ct))).ToList();
            if (lines.Any(line => line.LowestTotal is null)) throw new ConflictException("Existem itens sem nenhuma proposta disponível.", "agro360.quotation.incomplete_proposals");
            if (lines.Select(line => line.QuotationItemId).ToHashSet().SetEquals(command.Items.Select(line => line.QuotationItemId)) == false)
                throw new DomainException("A decisão deve cobrir exatamente os itens vigentes da cotação.", "agro360.quotation.decision_items_incomplete");
            var lowestByItem = lines.ToDictionary(line => line.QuotationItemId, line => line.LowestTotal!.Value);

            var proposals = (await conn.QueryAsync<DecisionProposal>(new CommandDefinition(
                """
                select qr.quotation_item_id QuotationItemId,qr.quotation_supplier_id QuotationSupplierId,qr.total Total,
                       qs.quotation_id QuotationId,qs.deleted_at is null SupplierActive
                from agro360.procurement_quotation_responses qr
                join agro360.procurement_quotation_suppliers qs on qs.tenant_id=qr.tenant_id and qs.id=qr.quotation_supplier_id
                join agro360.procurement_quotation_items qi on qi.tenant_id=qr.tenant_id and qi.id=qr.quotation_item_id and qi.deleted_at is null
                where qr.tenant_id=@TenantId and qi.quotation_id=@Id and qr.available and qr.deleted_at is null
                """,
                ScopeParams(new { Id = quotationId }), tx, cancellationToken: ct))).ToList();
            var proposalLookup = proposals
                .GroupBy(proposal => (proposal.QuotationItemId, proposal.QuotationSupplierId))
                .ToDictionary(group => group.Key, group => group.Max(proposal => proposal.Total));

            var justification = command.Justification?.Trim();
            var selectedRows = new List<(Guid QuotationItemId, Guid QuotationSupplierId, decimal SelectedTotal, decimal LowestTotal)>();
            foreach (var line in command.Items)
            {
                if (!proposalLookup.TryGetValue((line.QuotationItemId, line.QuotationSupplierId), out var selectedTotal))
                    throw new ConflictException("O fornecedor selecionado não possui proposta vigente para um dos itens.", "agro360.quotation.missing_proposal");
                var lowestTotal = lowestByItem[line.QuotationItemId];
                if (selectedTotal > lowestTotal && string.IsNullOrEmpty(justification))
                    throw new DomainException("A seleção acima do menor preço exige justificativa (mínimo de 3 caracteres).", "agro360.quotation.justification_required");
                selectedRows.Add((line.QuotationItemId, line.QuotationSupplierId, selectedTotal, lowestTotal));
            }
            if (selectedRows.Any(row => row.SelectedTotal > row.LowestTotal) && justification is { Length: < 3 })
                throw new DomainException("A seleção acima do menor preço exige justificativa (mínimo de 3 caracteres).", "agro360.quotation.justification_required");

            await conn.ExecuteAsync(new CommandDefinition(
                "update agro360.procurement_quotation_decisions set deleted_at=now(),updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and quotation_id=@Id and deleted_at is null",
                ScopeParams(new { Id = quotationId }), tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.procurement_quotation_decisions
                    (id, tenant_id, quotation_id, quotation_item_id, quotation_supplier_id, selected_total, lowest_total, justification, decided_at, decided_by, created_by, updated_by)
                values
                    (@Id, @TenantId, @QuotationId, @QuotationItemId, @QuotationSupplierId, @SelectedTotal, @LowestTotal, @Justification, now(), @UserId, @UserId, @UserId)
                """,
                selectedRows.Select(row => new
                {
                    Id = Guid.CreateVersion7(),
                    tenant.TenantId,
                    QuotationId = quotationId,
                    row.QuotationItemId,
                    row.QuotationSupplierId,
                    row.SelectedTotal,
                    row.LowestTotal,
                    Justification = row.SelectedTotal > row.LowestTotal ? justification : null
                }), tx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(
                "update agro360.procurement_quotations set status='APPROVED',decision_reason=@Justification,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id",
                ScopeParams(new { Id = quotationId, Justification = justification }), tx, cancellationToken: ct));

            await Audit(conn, tx, quotationId, "DECIDED", new { Items = selectedRows.Count, AboveLowest = selectedRows.Count(row => row.SelectedTotal > row.LowestTotal), Justification = justification }, ct);
        }, ct);
    }

    public async Task<IReadOnlyList<Guid>> ConvertToOrderAsync(Guid quotationId, CancellationToken ct = default)
    {
        var plan = await db.InTenantTransactionAsync<ConversionPlan>((conn, tx) => ConvertPlanAsync(conn, tx, quotationId, ct), ct);
        if (plan.ReplayOrderIds.Count > 0) return plan.ReplayOrderIds;

        var created = new List<Guid>();
        foreach (var draft in plan.Drafts)
        {
            try
            {
                created.Add(await procurement.CreateOrderAsync(new PurchaseOrderCommand(
                    draft.SupplierId,
                    plan.RequisitionId,
                    quotationId,
                    plan.CostCenterId,
                    plan.PropertyId,
                    draft.PaymentTerms,
                    draft.DeliveryOn,
                    draft.DeliveryAddress,
                    draft.Freight,
                    draft.Taxes,
                    draft.Lines), ct));
            }
            catch (ConflictException) when (created.Count == 0)
            {
                // Corrida: outra conversão criou o(s) pedido(s) entre o snapshot e a criação; reproduz o resultado.
                var raced = await FindConvertedOrderIdsAsync(quotationId, ct);
                if (raced.Count > 0) return raced;
                throw;
            }
        }

        await db.InTenantTransactionAsync(async (conn, tx) =>
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "update agro360.procurement_quotations set status='APPROVED',updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id and status in('PARTIAL','RESPONDED','ANALYSIS') and deleted_at is null",
                ScopeParams(new { Id = quotationId }), tx, cancellationToken: ct));
            await Audit(conn, tx, quotationId, "CONVERTED", new { OrderIds = created }, ct);
        }, ct);
        return created;
    }

    private async Task<ConversionPlan> ConvertPlanAsync(Npgsql.NpgsqlConnection conn, Npgsql.NpgsqlTransaction tx, Guid quotationId, CancellationToken ct)
    {
        var header = await conn.QuerySingleOrDefaultAsync<ConvertHeader>(new CommandDefinition(
            $"""
            select q.status Status,r.id RequisitionId,r.status RequisitionStatus,r.cost_center_id CostCenterId,r.property_id PropertyId,
                   r.number RequisitionNumber,q.number Number,r.needed_on NeededOn,coalesce(f.name,'unidade operacional') FarmName
            from agro360.procurement_quotations q
            join agro360.procurement_requisitions r on r.tenant_id=q.tenant_id and r.id=q.requisition_id and r.deleted_at is null
            left join agro360.geo_farms f on f.tenant_id=r.tenant_id and f.id=r.property_id and f.deleted_at is null
            where q.tenant_id=@TenantId and q.id=@Id and q.deleted_at is null {OperationalScopePolicy.Sql("r")}
            for update of q
            """,
            ScopeParams(new { Id = quotationId }), tx, cancellationToken: ct));
        if (header is null) throw new NotFoundException("Cotação", quotationId);
        if (!ConvertibleStatuses.Contains(header.Status))
            throw new ConflictException("Cotação inexistente ou sem propostas aptas para conversão.", "agro360.quotation.not_convertible");

        // Replay: pedidos já vinculados (não cancelados) são devolvidos em vez de criar duplicata.
        var existing = (await conn.QueryAsync<Guid>(new CommandDefinition(
            "select o.id from agro360.procurement_purchase_orders o where o.tenant_id=@TenantId and o.quotation_id=@Id and o.status<>'CANCELLED' and o.deleted_at is null order by o.created_at",
            ScopeParams(new { Id = quotationId }), tx, cancellationToken: ct))).AsList();
        if (existing.Count > 0)
            return new ConversionPlan(existing, header.RequisitionId, header.CostCenterId, header.PropertyId, Array.Empty<ConversionDraft>());

        if (header.RequisitionStatus is not ("APPROVED" or "PARTIALLY_FULFILLED"))
            throw new ConflictException("A requisição de origem mudou de status e não admite mais conversão.", "agro360.quotation.requisition_not_approved");

        var items = (await conn.QueryAsync<ConvertItem>(new CommandDefinition(
            """
            select qi.id ItemId,qi.catalog_item_id CatalogItemId,qi.quantity Quantity,qi.unit Unit,
                   qi.requisition_item_id RequisitionItemId,c.name ItemName
            from agro360.procurement_quotation_items qi
            join agro360.procurement_item_catalog c on c.tenant_id=qi.tenant_id and c.id=qi.catalog_item_id
            where qi.tenant_id=@TenantId and qi.quotation_id=@Id and qi.deleted_at is null
            order by qi.created_at,qi.id
            """,
            ScopeParams(new { Id = quotationId }), tx, cancellationToken: ct))).AsList();
        if (items.Count == 0) throw new ConflictException("A cotação não possui itens vigentes.", "agro360.quotation.no_winner");

        var proposals = (await conn.QueryAsync<ConvertProposal>(new CommandDefinition(
            """
            select qr.quotation_item_id ItemId,qr.quotation_supplier_id SupplierRowId,qs.supplier_id SupplierId,
                   qr.unit_price UnitPrice,qr.discount Discount,qr.total Total,qs.delivery_days DeliveryDays,
                   coalesce(nullif(trim(qs.payment_terms),''),'Conforme proposta registrada na cotação') PaymentTerms,
                   coalesce(qs.freight,0) Freight,coalesce(qs.taxes,0) Taxes,qs.proposal_valid_until ProposalValidUntil
            from agro360.procurement_quotation_responses qr
            join agro360.procurement_quotation_suppliers qs on qs.tenant_id=qr.tenant_id and qs.id=qr.quotation_supplier_id and qs.deleted_at is null
            join agro360.procurement_quotation_items qi on qi.tenant_id=qr.tenant_id and qi.id=qr.quotation_item_id and qi.deleted_at is null
            where qr.tenant_id=@TenantId and qi.quotation_id=@Id and qr.available and qr.deleted_at is null
            """,
            ScopeParams(new { Id = quotationId }), tx, cancellationToken: ct))).AsList();

        var decided = (await conn.QueryAsync<(Guid QuotationItemId, Guid QuotationSupplierId)>(new CommandDefinition(
            "select d.quotation_item_id,d.quotation_supplier_id from agro360.procurement_quotation_decisions d where d.tenant_id=@TenantId and d.quotation_id=@Id and d.deleted_at is null",
            ScopeParams(new { Id = quotationId }), tx, cancellationToken: ct)))
            .ToDictionary(row => row.QuotationItemId, row => row.QuotationSupplierId);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var chosen = new List<(ConvertItem Item, ConvertProposal Proposal)>();
        var autoDecisions = new List<AutoDecision>();
        foreach (var item in items)
        {
            var itemProposals = proposals
                .Where(proposal => proposal.ItemId == item.ItemId)
                .OrderBy(proposal => proposal.Total)
                .ThenBy(proposal => proposal.DeliveryDays ?? int.MaxValue)
                .ToList();
            if (itemProposals.Count == 0)
                throw new ConflictException($"O item '{item.ItemName}' não possui proposta vigente para conversão.", "agro360.quotation.no_winner");

            ConvertProposal proposal;
            if (decided.TryGetValue(item.ItemId, out var selectedRow))
            {
                proposal = itemProposals.FirstOrDefault(candidate => candidate.SupplierRowId == selectedRow)
                    ?? throw new ConflictException($"A decisão do item '{item.ItemName}' não possui proposta vigente.", "agro360.quotation.missing_proposal");
            }
            else
            {
                // Regra anterior sem decisão explícita: menor total (desempate por menor prazo).
                proposal = itemProposals[0];
                autoDecisions.Add(new AutoDecision(Guid.CreateVersion7(), item.ItemId, proposal, itemProposals.Min(candidate => candidate.Total)));
            }

            if (item.RequisitionItemId is null)
                throw new ConflictException($"O item '{item.ItemName}' não está vinculado a uma linha aprovada da requisição; reabra a cotação a partir da requisição.", "agro360.quotation.mapping_missing");
            chosen.Add((item, proposal));
        }

        // Persiste as decisões automáticas para que o histórico fique completo e auditável.
        if (autoDecisions.Count > 0)
            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.procurement_quotation_decisions
                    (id, tenant_id, quotation_id, quotation_item_id, quotation_supplier_id, selected_total, lowest_total, justification, decided_at, decided_by, created_by, updated_by)
                values
                    (@Id, @TenantId, @QuotationId, @QuotationItemId, @QuotationSupplierId, @SelectedTotal, @LowestTotal, null, now(), @UserId, @UserId, @UserId)
                """,
                autoDecisions.Select(decision => new
                {
                    decision.Id,
                    tenant.TenantId,
                    QuotationId = quotationId,
                    QuotationItemId = decision.ItemId,
                    QuotationSupplierId = decision.Proposal.SupplierRowId,
                    SelectedTotal = decision.Proposal.Total,
                    decision.Lowest,
                    tenant.UserId
                }), tx, cancellationToken: ct));

        var drafts = chosen
            .GroupBy(pair => pair.Proposal.SupplierId)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                // Prazo de entrega: dias prometidos na proposta do fornecedor; na ausência deles,
                // a data necessária da requisição (ou default operacional de 30 dias) — nunca a
                // validade de propostas da cotação.
                var promisedDays = group.Select(pair => pair.Proposal.DeliveryDays).Where(days => days.HasValue).Select(days => days!.Value).ToList();
                var deliveryOn = promisedDays.Count > 0
                    ? today.AddDays(promisedDays.Max())
                    : (header.NeededOn >= today ? header.NeededOn : today.AddDays(30));
                return new ConversionDraft(
                    group.Key,
                    group.Select(pair => new PurchaseOrderLineCommand(pair.Item.CatalogItemId, pair.Item.Quantity, pair.Item.Unit, pair.Proposal.UnitPrice, pair.Proposal.Discount, pair.Item.RequisitionItemId)).ToList(),
                    deliveryOn,
                    group.First().Proposal.PaymentTerms,
                    $"Entrega na unidade {header.FarmName} conforme cotação {header.Number}",
                    group.Max(pair => pair.Proposal.Freight),
                    group.Max(pair => pair.Proposal.Taxes));
            })
            .ToList();

        return new ConversionPlan(Array.Empty<Guid>(), header.RequisitionId, header.CostCenterId, header.PropertyId, drafts);
    }

    private Task<IReadOnlyList<Guid>> FindConvertedOrderIdsAsync(Guid quotationId, CancellationToken ct)
        => db.InTenantTransactionAsync<IReadOnlyList<Guid>>(async (conn, tx) =>
        {
            var ids = await conn.QueryAsync<Guid>(new CommandDefinition(
                "select o.id from agro360.procurement_purchase_orders o where o.tenant_id=@TenantId and o.quotation_id=@Id and o.status<>'CANCELLED' and o.deleted_at is null order by o.created_at",
                new { tenant.TenantId, Id = quotationId }, tx, cancellationToken: ct));
            return ids.AsList();
        }, ct);

    private DynamicParameters ScopeParams(object? extra = null)
    {
        var p = new DynamicParameters(extra);
        p.Add("TenantId", tenant.TenantId);
        p.Add("UserId", tenant.UserId);
        return p;
    }

    private async Task Audit(Npgsql.NpgsqlConnection conn, Npgsql.NpgsqlTransaction tx, Guid quotationId, string action, object data, CancellationToken ct)
        => await conn.ExecuteAsync(new CommandDefinition(
            "insert into agro360.procurement_audit_events(id,tenant_id,entity_type,entity_id,action,changed_fields,created_by,updated_by) values(gen_random_uuid(),@TenantId,'QUOTATION',@Id,@Action,@Data::jsonb,@UserId,@UserId)",
            new { tenant.TenantId, tenant.UserId, Id = quotationId, Action = action, Data = System.Text.Json.JsonSerializer.Serialize(data) },
            tx, cancellationToken: ct));

    private sealed record RequisitionSnapshot(Guid Id, string Status, DateOnly NeededOn, Guid? CostCenterId, Guid? PropertyId);
    private sealed record RequisitionRemainingLine(Guid RequisitionItemId, Guid CatalogItemId, string Unit, string ItemName, decimal RemainingQuantity);
    private sealed record ParticipantSnapshot(Guid Id, string Status);
    private sealed record QuoteItemRow(Guid Id, decimal Quantity, Guid? RequisitionItemId, string ItemName);
    private sealed record DecisionCandidate(Guid QuotationItemId, decimal? LowestTotal);
    private sealed record DecisionProposal(Guid QuotationItemId, Guid QuotationSupplierId, decimal Total, Guid QuotationId, bool SupplierActive);
    private sealed record ConvertHeader(Guid RequisitionId, string Status, string RequisitionStatus, string Number, string FarmName, DateOnly NeededOn, Guid? CostCenterId, Guid? PropertyId);
    private sealed record ConvertItem(Guid ItemId, Guid CatalogItemId, decimal Quantity, string Unit, Guid? RequisitionItemId, string ItemName);
    private sealed record ConvertProposal(Guid ItemId, Guid SupplierRowId, Guid SupplierId, decimal UnitPrice, decimal Discount, decimal Total, int? DeliveryDays, string PaymentTerms, decimal Freight, decimal Taxes, DateOnly? ProposalValidUntil);
    private sealed record AutoDecision(Guid Id, Guid ItemId, ConvertProposal Proposal, decimal Lowest);
    private sealed record ConversionPlan(IReadOnlyList<Guid> ReplayOrderIds, Guid RequisitionId, Guid? CostCenterId, Guid? PropertyId, IReadOnlyList<ConversionDraft> Drafts);
    private sealed record ConversionDraft(Guid SupplierId, IReadOnlyList<PurchaseOrderLineCommand> Lines, DateOnly DeliveryOn, string PaymentTerms, string DeliveryAddress, decimal Freight, decimal Taxes);
}
