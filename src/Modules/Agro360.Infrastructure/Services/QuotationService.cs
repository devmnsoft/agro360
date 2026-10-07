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

public sealed class QuotationService(DatabaseExecutor db, ITenantContext tenant) : IQuotationService
{
    public async Task<Guid> RequestQuotationsAsync(QuotationRequestCommand command, CancellationToken ct = default)
    {
        return await db.InTenantTransactionAsync<Guid>(async (conn, tx) =>
        {
            var req = await conn.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition(
                "select status from agro360.procurement_requisitions where tenant_id=@TenantId and id=@Id for update",
                new { tenant.TenantId, Id = command.RequisitionId }, tx, cancellationToken: ct))
                ?? throw new NotFoundException("Requisição", command.RequisitionId);

            if (req.status != "APPROVED") throw new DomainException("Somente requisições aprovadas podem solicitar cotações.", "agro360.quotation.requisition_not_approved");

            var quotationId = Guid.CreateVersion7();
            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.procurement_quotations
                    (id, tenant_id, requisition_id, status, requested_at, due_date, created_by, updated_by)
                values
                    (@Id, @TenantId, @RequisitionId, 'OPEN', now(), @DueDate, @UserId, @UserId)
                """,
                new { Id = quotationId, tenant.TenantId, command.RequisitionId, DueDate = command.DueDate, UserId = tenant.UserId },
                tx, cancellationToken: ct));

            foreach (var supplierId in command.SupplierIds)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    insert into agro360.procurement_quotation_suppliers(id, tenant_id, quotation_id, supplier_id, status)
                    values(gen_random_uuid(), @TenantId, @QuotationId, @SupplierId, 'PENDING')
                    """,
                    new { tenant.TenantId, QuotationId = quotationId, SupplierId = supplierId },
                    tx, cancellationToken: ct));
            }

            return quotationId;
        }, ct);
    }

    public async Task<Guid> SubmitQuotationAsync(SubmitQuotationCommand command, CancellationToken ct = default)
    {
        return await db.InTenantTransactionAsync<Guid>(async (conn, tx) =>
        {
            var quotationId = Guid.CreateVersion7(); // Simplificado para a implementação

            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.procurement_quotation_items(id, tenant_id, quotation_id, supplier_id, catalog_item_id, unit_price, delivery_days, notes, created_by, updated_by)
                values(@Id, @TenantId, @QuotationId, @SupplierId, @CatalogItemId, @UnitPrice, @DeliveryDays, @Notes, @UserId, @UserId)
                """,
                new { Id = quotationId, tenant.TenantId, command.QuotationId, command.SupplierId, command.CatalogItemId, command.UnitPrice, command.DeliveryDays, command.Notes, UserId = tenant.UserId },
                tx, cancellationToken: ct));

            return quotationId;
        }, ct);
    }

    public async Task<dynamic> CompareQuotationsAsync(Guid requisitionId, CancellationToken ct = default)
    {
        return await db.InTenantTransactionAsync<dynamic>(async (conn, tx) =>
        {
            var result = await conn.QueryAsync<dynamic>(new CommandDefinition(
                """
                select q.id, s.legal_name supplier, qi.unit_price, qi.delivery_days, qi.notes
                from agro360.procurement_quotation_items qi
                join agro360.procurement_quotations q on q.id = qi.quotation_id
                join agro360.procurement_suppliers s on s.id = qi.supplier_id
                where q.requisition_id = @RequisitionId and q.tenant_id = @TenantId
                order by qi.unit_price asc
                """,
                new { RequisitionId = requisitionId, tenant.TenantId },
                tx, cancellationToken: ct));
            return result.AsList();
        }, ct);
    }

    public async Task<Guid> ConvertToOrderAsync(Guid quotationId, CancellationToken ct = default)
    {
        // Esta lógica deve integrar com o ProcurementService.CreateOrderAsync
        // para evitar duplicação de código.
        throw new NotImplementedException("Integração com ProcurementService.CreateOrderAsync pendente.");
    }
}
