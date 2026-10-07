using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Dapper;

namespace Agro360.Infrastructure.Services;

public record MyDayItem(
    Guid Id,
    string Source,
    string Title,
    DateTimeOffset Deadline,
    string Priority,
    string SuggestedAction,
    string Link,
    string Category);

public interface IMyDayService
{
    Task<IReadOnlyList<MyDayItem>> GetPendingTasksAsync(CancellationToken ct = default);
}

public sealed class MyDayService(
    DatabaseExecutor db,
    ITenantContext tenant) : IMyDayService
{
    public async Task<IReadOnlyList<MyDayItem>> GetPendingTasksAsync(CancellationToken ct = default)
    {
        var items = new List<MyDayItem>();

        await db.InTenantTransactionAsync(async (conn, tx) =>
        {
            // 1. Requisições aguardando aprovação
            var reqs = await conn.QueryAsync<dynamic>(new CommandDefinition(
                """
                select r.id, r.number, r.needed_on, r.priority
                from agro360.procurement_requisitions r
                where r.tenant_id=@TenantId and r.status='AWAITING_APPROVAL' and r.deleted_at is null
                """,
                new { tenant.TenantId }, tx, cancellationToken: ct));
            foreach (var r in reqs) items.Add(new MyDayItem(
                r.id, "Procurement", $"Requisição {r.number} aguarda aprovação",
                r.needed_on.ToDateTimeOffset(), r.priority, "Aprovar/Rejeitar", $"/procurement/requisitions/{r.id}", "APPROVAL"));

            // 2. Pedidos com divergência no recebimento
            var orders = await conn.QueryAsync<dynamic>(new CommandDefinition(
                """
                select o.id, o.number, o.delivery_on
                from agro360.procurement_purchase_orders o
                where o.tenant_id=@TenantId and o.status in ('DIVERGENT', 'PARTIALLY_RECEIVED') and o.deleted_at is null
                """,
                new { tenant.TenantId }, tx, cancellationToken: ct));
            foreach (var o in orders) items.Add(new MyDayItem(
                o.id, "Procurement", $"Pedido {o.number} com pendência de recebimento",
                o.delivery_on.ToDateTimeOffset(), "MEDIUM", "Conferir Recebimento", $"/procurement/orders/{o.id}", "RECEIPT"));

            // 3. Itens em Quarentena (Qualidade)
            var quarantine = await conn.QueryAsync<dynamic>(new CommandDefinition(
                """
                select q.receipt_item_id as id, r.number as rec_num, q.created_at
                from agro360.procurement_receipt_quarantine q
                join agro360.procurement_receipts r on r.id = q.receipt_id
                where q.tenant_id=@TenantId and q.status='PENDING'
                """,
                new { tenant.TenantId }, tx, cancellationToken: ct));
            foreach (var q in quarantine) items.Add(new MyDayItem(
                q.id, "Quality", $"Item do recebimento {q.rec_num} aguarda inspeção",
                q.created_at.ToDateTimeOffset(), "HIGH", "Decidir Qualidade", $"/procurement/receipts/quality/{q.id}", "QUALITY"));

            // 4. Títulos Financeiros Vencidos (Apenas se tiver permissão financeira)
            var overdue = await conn.QueryAsync<dynamic>(new CommandDefinition(
                """
                select id, document, due_on, final_amount from agro360.finance_payables where tenant_id=@TenantId and status='OPEN' and due_on < current_date
                union all
                select id, document, due_on, final_amount from agro360.finance_receivables where tenant_id=@TenantId and status='OPEN' and due_on < current_date
                """,
                new { tenant.TenantId }, tx, cancellationToken: ct));
            foreach (var f in overdue) items.Add(new MyDayItem(
                f.id, "Finance", $"Título {f.document} vencido (Valor: {f.final_amount:N2})",
                f.due_on.ToDateTimeOffset(), "HIGH", "Regularizar", $"/finance/titles/{f.id}", "FINANCE"));

        }, ct);

        return items.OrderByDescending(x => x.Priority == "HIGH").ThenBy(x => x.Deadline).ToList();
    }
}
