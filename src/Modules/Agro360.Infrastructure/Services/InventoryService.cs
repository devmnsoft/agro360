using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Npgsql;

namespace Agro360.Infrastructure.Services;

public sealed class InventoryService(DatabaseExecutor database, ITenantContext tenantContext) : IInventoryService
{
    public Task<ProductDto> CreateProductAsync(CreateProductCommand command, CancellationToken cancellationToken)
    {
        var dto = new ProductDto(
            Guid.CreateVersion7(),
            Guard.Required(command.Sku, nameof(command.Sku), 60).ToUpperInvariant(),
            Guard.Required(command.Name, nameof(command.Name), 160),
            Guard.Required(command.Category, nameof(command.Category), 60).ToUpperInvariant(),
            Guard.Required(command.BaseUnit, nameof(command.BaseUnit), 16).ToLowerInvariant(),
            command.RequiresLot,
            command.IsPerishable);

        return database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into inventory.products
                    (id, tenant_id, sku, name, category, base_unit, requires_lot, is_perishable,
                     created_at, created_by, version)
                values
                    (@Id, @TenantId, @Sku, @Name, @Category, @BaseUnit, @RequiresLot, @IsPerishable,
                     now(), @CreatedBy, 1);
                """,
                new
                {
                    dto.Id,
                    tenantContext.TenantId,
                    dto.Sku,
                    dto.Name,
                    dto.Category,
                    dto.BaseUnit,
                    dto.RequiresLot,
                    dto.IsPerishable,
                    CreatedBy = tenantContext.UserId
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            await connection.WriteAuditAsync(
                transaction,
                tenantContext,
                "create",
                "Product",
                dto.Id,
                null,
                dto,
                cancellationToken).ConfigureAwait(false);
            return dto;
        }, cancellationToken);
    }

    public Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseCommand command, CancellationToken cancellationToken)
    {
        var dto = new WarehouseDto(
            Guid.CreateVersion7(),
            Guard.Required(command.FarmId, nameof(command.FarmId)),
            Guard.Required(command.Code, nameof(command.Code), 40).ToUpperInvariant(),
            Guard.Required(command.Name, nameof(command.Name), 160),
            Guard.Required(command.Type, nameof(command.Type), 40).ToUpperInvariant());

        return database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into inventory.warehouses
                    (id, tenant_id, farm_id, code, name, type, created_at, created_by, version)
                values
                    (@Id, @TenantId, @FarmId, @Code, @Name, @Type, now(), @CreatedBy, 1);
                """,
                new
                {
                    dto.Id,
                    tenantContext.TenantId,
                    dto.FarmId,
                    dto.Code,
                    dto.Name,
                    dto.Type,
                    CreatedBy = tenantContext.UserId
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            await connection.WriteAuditAsync(
                transaction,
                tenantContext,
                "create",
                "Warehouse",
                dto.Id,
                null,
                dto,
                cancellationToken).ConfigureAwait(false);
            return dto;
        }, cancellationToken);
    }

    public Task<StockMovementResult> ReceiveAsync(StockMovementCommand command, CancellationToken cancellationToken) =>
        database.InTenantTransactionAsync(
            (connection, transaction) => ApplyMovementAsync(connection, transaction, command, true, cancellationToken),
            cancellationToken);

    public Task<StockMovementResult> ConsumeAsync(StockMovementCommand command, CancellationToken cancellationToken) =>
        database.InTenantTransactionAsync(
            (connection, transaction) => ApplyMovementAsync(connection, transaction, command, false, cancellationToken),
            cancellationToken);

    public Task<PagedResult<StockBalanceDto>> ListBalancesAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        return database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
                """
                select count(*)
                from inventory.stock_balances b
                join inventory.products p on p.id = b.product_id and p.tenant_id = b.tenant_id
                where b.tenant_id = @TenantId
                  and (@Search is null or p.name ilike '%' || @Search || '%' or p.sku ilike '%' || @Search || '%');

                select b.warehouse_id as WarehouseId, b.product_id as ProductId,
                       p.sku, p.name as ProductName, b.unit, b.available, b.reserved,
                       b.minimum, b.average_cost as AverageCost, b.version
                from inventory.stock_balances b
                join inventory.products p on p.id = b.product_id and p.tenant_id = b.tenant_id
                where b.tenant_id = @TenantId
                  and (@Search is null or p.name ilike '%' || @Search || '%' or p.sku ilike '%' || @Search || '%')
                order by p.name
                limit @PageSize offset @Offset;
                """,
                new
                {
                    tenantContext.TenantId,
                    Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                    PageSize = pageSize,
                    Offset = (page - 1) * pageSize
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            var total = await grid.ReadSingleAsync<long>().ConfigureAwait(false);
            var items = (await grid.ReadAsync<StockBalanceDto>().ConfigureAwait(false)).ToArray();
            return new PagedResult<StockBalanceDto>(items, page, pageSize, total);
        }, cancellationToken);
    }

    private async Task<StockMovementResult> ApplyMovementAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StockMovementCommand command,
        bool isReceipt,
        CancellationToken cancellationToken)
    {
        var quantity = Guard.Positive(command.Quantity, nameof(command.Quantity));
        var unitCost = Guard.NonNegative(command.UnitCost, nameof(command.UnitCost));
        var unit = Guard.Required(command.Unit, nameof(command.Unit), 16).ToLowerInvariant();
        var movementType = isReceipt ? "RECEIPT" : "CONSUMPTION";

        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existing = await connection.QuerySingleOrDefaultAsync<StockMovementResult>(new CommandDefinition(
                """
                select id as MovementId, balance_after as NewBalance, average_cost_after as AverageCost, balance_version as Version
                from inventory.stock_movements
                where tenant_id = @TenantId and idempotency_key = @IdempotencyKey;
                """,
                new { tenantContext.TenantId, command.IdempotencyKey },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }
        }

        var product = await connection.QuerySingleOrDefaultAsync<ProductRuleRow>(new CommandDefinition(
            """
            select base_unit as BaseUnit, requires_lot as RequiresLot, is_perishable as IsPerishable
            from inventory.products
            where id = @ProductId and tenant_id = @TenantId and deleted_at is null;
            """,
            new { command.ProductId, tenantContext.TenantId },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false)
            ?? throw new NotFoundException("Produto", command.ProductId);

        if (!string.Equals(product.BaseUnit, unit, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("A unidade informada difere da unidade base do produto.", "inventory.unit_mismatch");
        }

        if (product.RequiresLot && string.IsNullOrWhiteSpace(command.LotNumber))
        {
            throw new DomainException("Este produto exige número de lote.", "inventory.lot_required");
        }

        if (product.IsPerishable && !command.ExpiresOn.HasValue)
        {
            throw new DomainException("Este produto exige data de validade.", "inventory.expiration_required");
        }

        var balance = await connection.QuerySingleOrDefaultAsync<BalanceRow>(new CommandDefinition(
            """
            select id, available, reserved, average_cost as AverageCost, version
            from inventory.stock_balances
            where tenant_id = @TenantId and warehouse_id = @WarehouseId and product_id = @ProductId
            for update;
            """,
            new { tenantContext.TenantId, command.WarehouseId, command.ProductId },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (balance is null)
        {
            if (!isReceipt)
            {
                throw new ConflictException("Não existe saldo para o produto solicitado.", "inventory.insufficient_stock");
            }

            balance = new BalanceRow
            {
                Id = Guid.CreateVersion7(),
                Available = 0,
                Reserved = 0,
                AverageCost = 0,
                Version = 0
            };
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into inventory.stock_balances
                    (id, tenant_id, warehouse_id, product_id, unit, available, reserved, minimum,
                     average_cost, created_at, updated_at, version)
                values
                    (@Id, @TenantId, @WarehouseId, @ProductId, @Unit, 0, 0, 0, 0, now(), now(), 0);
                """,
                new
                {
                    balance.Id,
                    tenantContext.TenantId,
                    command.WarehouseId,
                    command.ProductId,
                    Unit = unit
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        decimal newBalance;
        decimal newAverageCost;
        if (isReceipt)
        {
            newBalance = balance.Available + quantity;
            newAverageCost = newBalance == 0
                ? 0
                : decimal.Round(((balance.Available * balance.AverageCost) + (quantity * unitCost)) / newBalance, 4);
        }
        else
        {
            if (balance.Available - balance.Reserved < quantity)
            {
                throw new ConflictException("Saldo disponível insuficiente; estoque negativo não é permitido.", "inventory.insufficient_stock");
            }

            newBalance = balance.Available - quantity;
            newAverageCost = balance.AverageCost;
        }

        var newVersion = balance.Version + 1;
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            update inventory.stock_balances
            set available = @NewBalance,
                average_cost = @NewAverageCost,
                updated_at = now(),
                version = @NewVersion
            where id = @Id and tenant_id = @TenantId and version = @ExpectedVersion;
            """,
            new
            {
                balance.Id,
                tenantContext.TenantId,
                NewBalance = newBalance,
                NewAverageCost = newAverageCost,
                NewVersion = newVersion,
                ExpectedVersion = balance.Version
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (updated != 1)
        {
            throw new ConflictException("O saldo foi alterado por outra operação.");
        }

        var movementId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into inventory.stock_movements
                (id, tenant_id, warehouse_id, product_id, movement_type, quantity, unit,
                 unit_cost, total_cost, lot_number, expires_on, reference_type, reference_id,
                 notes, idempotency_key, balance_after, average_cost_after, balance_version,
                 occurred_at, created_by)
            values
                (@Id, @TenantId, @WarehouseId, @ProductId, @MovementType, @Quantity, @Unit,
                 @UnitCost, @TotalCost, @LotNumber, @ExpiresOn, @ReferenceType, @ReferenceId,
                 @Notes, @IdempotencyKey, @BalanceAfter, @AverageCostAfter, @BalanceVersion,
                 now(), @CreatedBy);
            """,
            new
            {
                Id = movementId,
                tenantContext.TenantId,
                command.WarehouseId,
                command.ProductId,
                MovementType = movementType,
                Quantity = quantity,
                Unit = unit,
                UnitCost = isReceipt ? unitCost : balance.AverageCost,
                TotalCost = quantity * (isReceipt ? unitCost : balance.AverageCost),
                command.LotNumber,
                command.ExpiresOn,
                ReferenceType = Guard.Required(command.ReferenceType, nameof(command.ReferenceType), 60),
                command.ReferenceId,
                command.Notes,
                command.IdempotencyKey,
                BalanceAfter = newBalance,
                AverageCostAfter = newAverageCost,
                BalanceVersion = newVersion,
                CreatedBy = tenantContext.UserId
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var result = new StockMovementResult(movementId, newBalance, newAverageCost, newVersion);
        await connection.WriteAuditAsync(
            transaction,
            tenantContext,
            movementType.ToLowerInvariant(),
            "StockMovement",
            movementId,
            null,
            result,
            cancellationToken).ConfigureAwait(false);
        await connection.EnqueueAsync(
            transaction,
            tenantContext.TenantId,
            isReceipt ? "StockReceived" : "StockConsumed",
            movementId,
            result,
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    private sealed class ProductRuleRow
    {
        public string BaseUnit { get; init; } = string.Empty;

        public bool RequiresLot { get; init; }

        public bool IsPerishable { get; init; }
    }

    private sealed class BalanceRow
    {
        public Guid Id { get; init; }

        public decimal Available { get; init; }

        public decimal Reserved { get; init; }

        public decimal AverageCost { get; init; }

        public long Version { get; init; }
    }
}
