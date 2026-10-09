using System;
using System.Threading;
using System.Threading.Tasks;
using Agro360.Application.Contracts;
using Npgsql;

namespace Agro360.Infrastructure.Services;

/// <summary>
/// Núcleo canônico de criação de pedido de compra, exposto para operações que precisam criar
/// vários pedidos dentro de uma única transação própria (ex.: conversão de cotação multi-fornecedor).
/// Reaproveita exatamente as mesmas regras (status do fornecedor, unidade operacional, saldo
/// autorizado sob lock FOR UPDATE) sem duplicação de lógica e sem fatiar o commit em várias
/// transações. O chamador é responsável pela transação e pelo app.tenant_id já configurado.
/// </summary>
public interface IPurchaseOrderKernel
{
    Task<Guid> CreateOrderWithinTransactionAsync(NpgsqlConnection c, NpgsqlTransaction t, PurchaseOrderCommand x, CancellationToken ct);
}
