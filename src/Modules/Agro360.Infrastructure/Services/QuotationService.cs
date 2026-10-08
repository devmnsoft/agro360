using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;

namespace Agro360.Infrastructure.Services;

public interface IQuotationService
{
    Task<Guid> RequestQuotationsAsync(QuotationRequestCommand command, CancellationToken ct = default);
    Task<Guid> SubmitQuotationAsync(SubmitQuotationCommand command, CancellationToken ct = default);
    Task<dynamic> CompareQuotationsAsync(Guid requisitionId, CancellationToken ct = default);
    Task<Guid> ConvertToOrderAsync(Guid quotationId, CancellationToken ct = default);
}

public sealed class QuotationService(DatabaseExecutor db, ITenantContext tenant, IProcurementService procurement) : IQuotationService
{
    public async Task<Guid> RequestQuotationsAsync(QuotationRequestCommand command, CancellationToken ct = default)
    {
        return await db.InTenantTransactionAsync<Guid>(async (conn, tx) =>
        {
            var req = await conn.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition(
                "select status from agro360.procurement_requisitions where tenant_id=@TenantId and id=@Id and deleted_at is null for update",
                new { tenant.TenantId, Id = command.RequisitionId }, tx, cancellationToken: ct))
                ?? throw new NotFoundException("Requisição", command.RequisitionId);

            if (req.status != "APPROVED") throw new DomainException("Somente requisições aprovadas podem solicitar cotações.", "agro360.quotation.requisition_not_approved");
            if (command.DueDate <= DateOnly.FromDateTime(DateTime.UtcNow)) throw new DomainException("Prazo da cotação deve ser futuro.", "agro360.quotation.due_date_invalid");
            if (command.SupplierIds.Count == 0) throw new DomainException("Informe ao menos um fornecedor participante.", "agro360.quotation.suppliers_required");

            var quotationId = Guid.CreateVersion7();
            var number = await conn.ExecuteScalarAsync<long>(new CommandDefinition("select nextval('agro360.procurement_document_number_seq')", transaction: tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.procurement_quotations
                    (id, tenant_id, number, requisition_id, status, valid_until, created_by, updated_by)
                values
                    (@Id, @TenantId, @Number, @RequisitionId, 'SENT', @DueDate, @UserId, @UserId)
                """,
                new { Id = quotationId, tenant.TenantId, Number = $"COT-{DateTime.UtcNow:yyyy}-{number:000000}", command.RequisitionId, DueDate = command.DueDate, UserId = tenant.UserId },
                tx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.procurement_quotation_items(id,tenant_id,quotation_id,catalog_item_id,quantity,unit,created_by,updated_by)
                select gen_random_uuid(),tenant_id,@QuotationId,catalog_item_id,quantity,unit,@UserId,@UserId
                from agro360.procurement_requisition_items
                where tenant_id=@TenantId and requisition_id=@RequisitionId and deleted_at is null
                """,
                new { tenant.TenantId, QuotationId = quotationId, command.RequisitionId, UserId = tenant.UserId },
                tx, cancellationToken: ct));

            foreach (var supplierId in command.SupplierIds.Distinct())
            {
                var supplierValid = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "select exists(select 1 from agro360.procurement_suppliers where tenant_id=@TenantId and id=@SupplierId and status in('ACTIVE','APPROVED') and deleted_at is null)",
                    new { tenant.TenantId, SupplierId = supplierId },
                    tx,
                    cancellationToken: ct));
                if (!supplierValid) throw new DomainException("Fornecedor participante inexistente, inativo ou não elegível.", "agro360.quotation.supplier_invalid");
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    insert into agro360.procurement_quotation_suppliers(id, tenant_id, quotation_id, supplier_id, status, created_by, updated_by)
                    values(gen_random_uuid(), @TenantId, @QuotationId, @SupplierId, 'PENDING', @UserId, @UserId)
                    """,
                    new { tenant.TenantId, QuotationId = quotationId, SupplierId = supplierId, UserId = tenant.UserId },
                    tx, cancellationToken: ct));
            }

            return quotationId;
        }, ct);
    }

    public async Task<Guid> SubmitQuotationAsync(SubmitQuotationCommand command, CancellationToken ct = default)
    {
        return await db.InTenantTransactionAsync<Guid>(async (conn, tx) =>
        {
            if (command.UnitPrice <= 0 || command.DeliveryDays < 0) throw new DomainException("Preço e prazo da proposta são inválidos.", "agro360.quotation.response_invalid");
            var participant = await conn.QuerySingleOrDefaultAsync<(Guid Id, string Status)>(new CommandDefinition(
                """
                select qs.id, q.status
                from agro360.procurement_quotation_suppliers qs
                join agro360.procurement_quotations q on q.tenant_id=qs.tenant_id and q.id=qs.quotation_id
                where qs.tenant_id=@TenantId and qs.quotation_id=@QuotationId and qs.supplier_id=@SupplierId
                  and qs.deleted_at is null and q.deleted_at is null
                for update of qs,q
                """,
                new { tenant.TenantId, command.QuotationId, command.SupplierId },
                tx,
                cancellationToken: ct));
            if (participant.Id == Guid.Empty) throw new DomainException("Fornecedor não participa desta cotação.", "agro360.quotation.supplier_not_participant");
            if (participant.Status is not ("SENT" or "PARTIAL" or "RESPONDED" or "ANALYSIS")) throw new ConflictException("A cotação não aceita novas propostas.", "agro360.quotation.closed");

            var item = await conn.QuerySingleOrDefaultAsync<(Guid Id, decimal Quantity)>(new CommandDefinition(
                "select id,quantity from agro360.procurement_quotation_items where tenant_id=@TenantId and quotation_id=@QuotationId and catalog_item_id=@CatalogItemId and deleted_at is null",
                new { tenant.TenantId, command.QuotationId, command.CatalogItemId },
                tx,
                cancellationToken: ct));
            if (item.Id == Guid.Empty) throw new DomainException("Item não pertence à cotação.", "agro360.quotation.item_invalid");

            var responseId = Guid.CreateVersion7();
            var total = decimal.Round(item.Quantity * command.UnitPrice, 2, MidpointRounding.AwayFromZero);
            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.procurement_quotation_responses(id,tenant_id,quotation_item_id,quotation_supplier_id,unit_price,total,available,notes,created_by,updated_by)
                values(@Id,@TenantId,@QuotationItemId,@QuotationSupplierId,@UnitPrice,@Total,true,@Notes,@UserId,@UserId);
                update agro360.procurement_quotation_suppliers
                   set status='RESPONDED', delivery_days=@DeliveryDays, updated_at=now(), updated_by=@UserId
                 where tenant_id=@TenantId and id=@QuotationSupplierId;
                update agro360.procurement_quotations
                   set status='PARTIAL', updated_at=now(), updated_by=@UserId
                 where tenant_id=@TenantId and id=@QuotationId and status='SENT';
                """,
                new { Id = responseId, tenant.TenantId, QuotationItemId = item.Id, QuotationSupplierId = participant.Id, command.UnitPrice, Total = total, command.DeliveryDays, command.Notes, command.QuotationId, UserId = tenant.UserId },
                tx,
                cancellationToken: ct));

            return responseId;
        }, ct);
    }

    public async Task<dynamic> CompareQuotationsAsync(Guid requisitionId, CancellationToken ct = default)
    {
        return await db.InTenantTransactionAsync<dynamic>(async (conn, tx) =>
        {
            var result = await conn.QueryAsync<dynamic>(new CommandDefinition(
                """
                select q.id quotation_id,q.number,c.name item,s.legal_name supplier,qi.quantity,qi.unit,qr.unit_price,qr.discount,qr.total,qs.delivery_days,qr.notes,
                       min(qr.total) over(partition by qi.id) lowest_total
                from agro360.procurement_quotations q
                join agro360.procurement_quotation_items qi on qi.tenant_id=q.tenant_id and qi.quotation_id=q.id and qi.deleted_at is null
                join agro360.procurement_item_catalog c on c.tenant_id=qi.tenant_id and c.id=qi.catalog_item_id
                join agro360.procurement_quotation_responses qr on qr.tenant_id=qi.tenant_id and qr.quotation_item_id=qi.id and qr.available and qr.deleted_at is null
                join agro360.procurement_quotation_suppliers qs on qs.tenant_id=qr.tenant_id and qs.id=qr.quotation_supplier_id
                join agro360.procurement_suppliers s on s.tenant_id=qs.tenant_id and s.id=qs.supplier_id
                where q.requisition_id = @RequisitionId and q.tenant_id = @TenantId and q.deleted_at is null
                order by qi.created_at,qr.total asc,s.legal_name
                """,
                new { RequisitionId = requisitionId, tenant.TenantId },
                tx, cancellationToken: ct));
            return result.AsList();
        }, ct);
    }

    public async Task<Guid> ConvertToOrderAsync(Guid quotationId, CancellationToken ct = default)
    {
        var snapshot = await db.InTenantTransactionAsync(async (conn, tx) =>
        {
            var lockedQuotation = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "select id from agro360.procurement_quotations where tenant_id=@TenantId and id=@QuotationId and deleted_at is null for update",
                new { tenant.TenantId, QuotationId = quotationId },
                tx,
                cancellationToken: ct));
            if (lockedQuotation is null) throw new NotFoundException("Cotação", quotationId);

            var existing = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "select id from agro360.procurement_purchase_orders where tenant_id=@TenantId and quotation_id=@QuotationId and status<>'CANCELLED' and deleted_at is null order by created_at limit 1",
                new { tenant.TenantId, QuotationId = quotationId },
                tx,
                cancellationToken: ct));
            if (existing is not null) return new QuotationConversionSnapshot(existing.Value, Guid.Empty, Guid.Empty, null, null, "", DateOnly.FromDateTime(DateTime.UtcNow), Array.Empty<PurchaseOrderLineCommand>(), true);

            var header = await conn.QuerySingleOrDefaultAsync<QuotationHeader>(new CommandDefinition(
                """
                select q.requisition_id RequisitionId,r.cost_center_id CostCenterId,r.property_id PropertyId,coalesce(f.name,'Unidade operacional') FarmName,q.valid_until ValidUntil
                from agro360.procurement_quotations q
                join agro360.procurement_requisitions r on r.tenant_id=q.tenant_id and r.id=q.requisition_id
                left join agro360.geo_farms f on f.tenant_id=r.tenant_id and f.id=r.property_id
                where q.tenant_id=@TenantId and q.id=@QuotationId and q.status in('PARTIAL','RESPONDED','ANALYSIS','APPROVED') and q.deleted_at is null
                """,
                new { tenant.TenantId, QuotationId = quotationId },
                tx,
                cancellationToken: ct));
            if (header is null) throw new ConflictException("Cotação inexistente ou sem propostas aptas para conversão.", "agro360.quotation.not_convertible");

            var winners = (await conn.QueryAsync<QuotationWinner>(new CommandDefinition(
                """
                with ranked as (
                    select qi.id quotation_item_id,qi.catalog_item_id,qi.quantity,qi.unit,ri.id requisition_item_id,qs.supplier_id,qr.unit_price,qr.discount,qr.total,
                           row_number() over(partition by qi.id order by qr.total,qs.delivery_days nulls last,qr.created_at) ranking
                    from agro360.procurement_quotation_items qi
                    join agro360.procurement_quotations q on q.tenant_id=qi.tenant_id and q.id=qi.quotation_id
                    join agro360.procurement_requisition_items ri on ri.tenant_id=q.tenant_id and ri.requisition_id=q.requisition_id and ri.catalog_item_id=qi.catalog_item_id and upper(ri.unit)=upper(qi.unit) and ri.deleted_at is null
                    join agro360.procurement_quotation_responses qr on qr.tenant_id=qi.tenant_id and qr.quotation_item_id=qi.id and qr.available and qr.deleted_at is null
                    join agro360.procurement_quotation_suppliers qs on qs.tenant_id=qr.tenant_id and qs.id=qr.quotation_supplier_id and qs.deleted_at is null
                    where qi.tenant_id=@TenantId and qi.quotation_id=@QuotationId and qi.deleted_at is null
                )
                select * from ranked where ranking=1 order by quotation_item_id
                """,
                new { tenant.TenantId, QuotationId = quotationId },
                tx,
                cancellationToken: ct))).AsList();
            if (winners.Count == 0) throw new ConflictException("Cotação não possui proposta elegível.", "agro360.quotation.no_winner");
            if (winners.Select(w => w.SupplierId).Distinct().Count() != 1) throw new ConflictException("A conversão automática exige um único fornecedor vencedor. Gere pedidos separados para múltiplos fornecedores.", "agro360.quotation.multiple_winners");

            return new QuotationConversionSnapshot(
                Guid.Empty,
                header.RequisitionId,
                winners[0].SupplierId,
                header.CostCenterId,
                header.PropertyId,
                $"Entrega conforme cotação {quotationId:N} - {header.FarmName}",
                header.ValidUntil ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                winners.Select(w => new PurchaseOrderLineCommand(w.CatalogItemId, w.Quantity, w.Unit, w.UnitPrice, w.Discount, w.RequisitionItemId)).ToArray(),
                false);
        }, ct);

        if (snapshot.IsReplay) return snapshot.ExistingOrderId;
        return await procurement.CreateOrderAsync(new PurchaseOrderCommand(snapshot.SupplierId, snapshot.RequisitionId, quotationId, snapshot.CostCenterId, snapshot.PropertyId, "Conforme proposta registrada na cotação", snapshot.DeliveryOn, snapshot.DeliveryAddress, 0, 0, snapshot.Items), ct);
    }

    private sealed record QuotationConversionSnapshot(Guid ExistingOrderId, Guid RequisitionId, Guid SupplierId, Guid? CostCenterId, Guid? PropertyId, string DeliveryAddress, DateOnly DeliveryOn, IReadOnlyList<PurchaseOrderLineCommand> Items, bool IsReplay);
    private sealed record QuotationHeader(Guid RequisitionId, Guid? CostCenterId, Guid? PropertyId, string FarmName, DateOnly? ValidUntil);
    private sealed record QuotationWinner(Guid CatalogItemId, Guid RequisitionItemId, Guid SupplierId, decimal Quantity, string Unit, decimal UnitPrice, decimal Discount);
}
