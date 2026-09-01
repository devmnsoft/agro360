using Agro360.Application.Contracts;
using Agro360.Domain.Commercial;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;

namespace Agro360.Infrastructure.Services;

public sealed class CommercialService(DatabaseExecutor database, ITenantContext tenantContext) : ICommercialService
{
    public Task<SaleResult> CreateAndConfirmSaleAsync(CreateSaleCommand command, CancellationToken cancellationToken)
    {
        var productType = Guard.Required(command.ProductType, nameof(command.ProductType), 40).ToUpperInvariant();
        if (productType is not ("CROP" or "ANIMAL"))
        {
            throw new DomainException(
                "A fatia atual aceita venda de CROP ou ANIMAL.",
                "agro360.commercial_product_type_not_supported");
        }

        if (productType == "ANIMAL"
            && (command.Quantity != 1 || !command.Unit.Equals("head", StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException(
                "A venda de animal individual exige quantidade 1 e unidade 'head'.",
                "agro360.commercial_invalid_individual_animal_quantity");
        }

        var sale = Sale.Create(
            tenantContext.TenantId,
            command.FarmId,
            productType,
            command.OriginId,
            command.Quantity,
            command.Unit,
            new Money(command.UnitPrice, command.Currency),
            command.DueDate);
        sale.Confirm();

        return database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                var existing = await connection.QuerySingleOrDefaultAsync<SaleResult>(new CommandDefinition(
                    """
                    select s.id as SaleId, r.id as ReceivableId, s.total_amount as TotalAmount,
                           s.currency, s.status, n.id as TraceabilityNodeId
                    from agro360.commercial_sales s
                    join agro360.finance_receivables r on r.sale_id = s.id and r.tenant_id = s.tenant_id
                    join agro360.traceability_nodes n on n.entity_id = s.id and n.entity_type = 'SALE' and n.tenant_id = s.tenant_id
                    where s.tenant_id = @TenantId and s.idempotency_key = @IdempotencyKey;
                    """,
                    new { tenantContext.TenantId, command.IdempotencyKey },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
                if (existing is not null)
                {
                    return existing;
                }
            }

            Guid? stockMovementId = null;
            if (productType == "CROP")
            {
                if (!command.WarehouseId.HasValue)
                {
                    throw new DomainException("A venda de produto colhido exige o depósito de origem.", "agro360.commercial_warehouse_required");
                }

                var balance = await connection.QuerySingleOrDefaultAsync<BalanceRow>(new CommandDefinition(
                    """
                    select id, unit, available, reserved, average_cost as AverageCost, version
                    from agro360.inventory_stock_balances
                    where tenant_id = @TenantId and warehouse_id = @WarehouseId and product_id = @ProductId
                    for update;
                    """,
                    new
                    {
                        tenantContext.TenantId,
                        WarehouseId = command.WarehouseId.Value,
                        ProductId = command.OriginId
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false)
                    ?? throw new ConflictException("Produto sem saldo para venda.", "agro360.inventory_insufficient_stock");
                if (!string.Equals(balance.Unit, command.Unit, StringComparison.OrdinalIgnoreCase))
                {
                    throw new DomainException("A unidade da venda difere do estoque.", "agro360.inventory_unit_mismatch");
                }

                if (balance.Available - balance.Reserved < command.Quantity)
                {
                    throw new ConflictException("Saldo disponível insuficiente para venda.", "agro360.inventory_insufficient_stock");
                }

                var newBalance = balance.Available - command.Quantity;
                var affected = await connection.ExecuteAsync(new CommandDefinition(
                    """
                    update agro360.inventory_stock_balances
                    set available = @NewBalance, updated_at = now(), version = version + 1
                    where id = @Id and tenant_id = @TenantId and version = @Version;
                    """,
                    new
                    {
                        NewBalance = newBalance,
                        balance.Id,
                        tenantContext.TenantId,
                        balance.Version
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
                if (affected != 1)
                {
                    throw new ConflictException("O saldo foi alterado por outra operação.");
                }

                stockMovementId = Guid.CreateVersion7();
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    insert into agro360.inventory_stock_movements
                        (id, tenant_id, warehouse_id, product_id, movement_type, quantity, unit,
                         unit_cost, total_cost, reference_type, reference_id, idempotency_key,
                         balance_after, average_cost_after, balance_version, occurred_at, created_by)
                    values
                        (@Id, @TenantId, @WarehouseId, @ProductId, 'SALE', @Quantity, @Unit,
                         @UnitCost, @TotalCost, 'SALE', @SaleId, @IdempotencyKey,
                         @BalanceAfter, @UnitCost, @BalanceVersion, now(), @CreatedBy);
                    """,
                    new
                    {
                        Id = stockMovementId.Value,
                        tenantContext.TenantId,
                        WarehouseId = command.WarehouseId.Value,
                        ProductId = command.OriginId,
                        command.Quantity,
                        Unit = command.Unit.ToLowerInvariant(),
                        UnitCost = balance.AverageCost,
                        TotalCost = command.Quantity * balance.AverageCost,
                        SaleId = sale.Id,
                        IdempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
                            ? null
                            : $"stock:{command.IdempotencyKey}",
                        BalanceAfter = newBalance,
                        BalanceVersion = balance.Version + 1,
                        CreatedBy = tenantContext.UserId
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
            else
            {
                var animal = await connection.QuerySingleOrDefaultAsync<AnimalSaleRow>(new CommandDefinition(
                    """
                    select status, withdrawal_until as WithdrawalUntil, version
                    from agro360.livestock_animals
                    where id = @AnimalId and tenant_id = @TenantId and farm_id = @FarmId and deleted_at is null
                    for update;
                    """,
                    new { AnimalId = command.OriginId, tenantContext.TenantId, command.FarmId },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false)
                    ?? throw new NotFoundException("Animal", command.OriginId);
                if (animal.Status != 1)
                {
                    throw new ConflictException("Somente animal ativo pode ser vendido.", "agro360.livestock_animal_not_active");
                }

                var saleDate = DateOnly.FromDateTime(DateTime.UtcNow);
                if (animal.WithdrawalUntil.HasValue && saleDate <= animal.WithdrawalUntil.Value)
                {
                    throw new ConflictException(
                        $"O animal está em carência sanitária até {animal.WithdrawalUntil:dd/MM/yyyy}.",
                        "agro360.livestock_withdrawal_period_active");
                }

                var affected = await connection.ExecuteAsync(new CommandDefinition(
                    """
                    update agro360.livestock_animals
                    set status = 3, updated_at = now(), updated_by = @UserId, version = version + 1
                    where id = @AnimalId and tenant_id = @TenantId and version = @Version;
                    """,
                    new
                    {
                        AnimalId = command.OriginId,
                        tenantContext.TenantId,
                        UserId = tenantContext.UserId,
                        animal.Version
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
                if (affected != 1)
                {
                    throw new ConflictException("O animal foi alterado por outra operação.");
                }
            }

            var receivableId = Guid.CreateVersion7();
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.commercial_sales
                    (id, tenant_id, farm_id, product_type, origin_id, warehouse_id,
                     quantity, unit, unit_price, total_amount, currency, buyer_name,
                     buyer_document, due_date, status, idempotency_key, confirmed_at,
                     created_at, created_by, version)
                values
                    (@Id, @TenantId, @FarmId, @ProductType, @OriginId, @WarehouseId,
                     @Quantity, @Unit, @UnitPrice, @TotalAmount, @Currency, @BuyerName,
                     @BuyerDocument, @DueDate, 'CONFIRMED', @IdempotencyKey, now(),
                     now(), @CreatedBy, 1);

                insert into agro360.finance_receivables
                    (id, tenant_id, farm_id, sale_id, description, amount, currency,
                     due_date, status, created_at, created_by, version)
                values
                    (@ReceivableId, @TenantId, @FarmId, @Id,
                     'Venda ' || @ProductType || ' para ' || @BuyerName,
                     @TotalAmount, @Currency, @DueDate, 'OPEN', now(), @CreatedBy, 1);
                """,
                new
                {
                    sale.Id,
                    tenantContext.TenantId,
                    sale.FarmId,
                    ProductType = productType,
                    sale.OriginId,
                    command.WarehouseId,
                    sale.Quantity,
                    Unit = sale.Unit.ToLowerInvariant(),
                    sale.UnitPrice,
                    sale.TotalAmount,
                    sale.Currency,
                    BuyerName = Guard.Required(command.BuyerName, nameof(command.BuyerName), 160),
                    command.BuyerDocument,
                    sale.DueDate,
                    command.IdempotencyKey,
                    CreatedBy = tenantContext.UserId,
                    ReceivableId = receivableId
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var originNodeId = await UpsertNodeAsync(
                connection,
                transaction,
                productType == "CROP" ? "PRODUCT" : "ANIMAL",
                command.OriginId,
                productType == "ANIMAL" ? "Animal vendido" : "Produto agrícola",
                cancellationToken).ConfigureAwait(false);
            var saleNodeId = await UpsertNodeAsync(
                connection,
                transaction,
                "SALE",
                sale.Id,
                $"Venda para {command.BuyerName}",
                cancellationToken).ConfigureAwait(false);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.traceability_edges
                    (id, tenant_id, from_node_id, to_node_id, relation_type, created_at)
                values
                    (@Id, @TenantId, @OriginNodeId, @SaleNodeId, 'SOLD_IN', now())
                on conflict (tenant_id, from_node_id, to_node_id, relation_type) do nothing;
                """,
                new
                {
                    Id = Guid.CreateVersion7(),
                    tenantContext.TenantId,
                    OriginNodeId = originNodeId,
                    SaleNodeId = saleNodeId
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var result = new SaleResult(
                sale.Id,
                receivableId,
                sale.TotalAmount,
                sale.Currency,
                "CONFIRMED",
                saleNodeId);
            await connection.WriteAuditAsync(
                transaction,
                tenantContext,
                "confirm",
                "Sale",
                sale.Id,
                null,
                new { result, StockMovementId = stockMovementId },
                cancellationToken).ConfigureAwait(false);
            await connection.EnqueueAsync(
                transaction,
                tenantContext.TenantId,
                "SaleConfirmed",
                sale.Id,
                result,
                cancellationToken).ConfigureAwait(false);
            return result;
        }, cancellationToken);
    }

    private Task<Guid> UpsertNodeAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        string entityType,
        Guid entityId,
        string label,
        CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            insert into agro360.traceability_nodes (id, tenant_id, entity_type, entity_id, label, created_at)
            values (@Id, @TenantId, @EntityType, @EntityId, @Label, now())
            on conflict (tenant_id, entity_type, entity_id)
            do update set label = excluded.label
            returning id;
            """,
            new { Id = Guid.CreateVersion7(), tenantContext.TenantId, EntityType = entityType, EntityId = entityId, Label = label },
            transaction,
            cancellationToken: cancellationToken));

    private sealed class BalanceRow
    {
        public Guid Id { get; init; }

        public string Unit { get; init; } = string.Empty;

        public decimal Available { get; init; }

        public decimal Reserved { get; init; }

        public decimal AverageCost { get; init; }

        public long Version { get; init; }
    }

    private sealed class AnimalSaleRow
    {
        public short Status { get; init; }

        public DateOnly? WithdrawalUntil { get; init; }

        public long Version { get; init; }
    }
}
