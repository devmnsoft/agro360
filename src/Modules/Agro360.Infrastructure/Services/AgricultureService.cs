using Agro360.Application.Contracts;
using Agro360.Domain.Agriculture;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Npgsql;

namespace Agro360.Infrastructure.Services;

public sealed class AgricultureService(DatabaseExecutor database, ITenantContext tenantContext) : IAgricultureService
{
    public Task<SeasonDto> CreateSeasonAsync(CreateSeasonCommand command, CancellationToken cancellationToken)
    {
        var season = Season.Create(
            tenantContext.TenantId,
            command.FarmId,
            command.Name,
            command.Crop,
            command.StartDate,
            command.EndDate);
        season.Plan(command.PlannedAreaHa, command.ExpectedYieldPerHa);

        return database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.agriculture_seasons
                    (id, tenant_id, farm_id, name, crop, start_date, end_date, status,
                     planned_area_ha, expected_yield_per_ha, created_at, created_by, version)
                values
                    (@Id, @TenantId, @FarmId, @Name, @Crop, @StartDate, @EndDate, @Status,
                     @PlannedAreaHa, @ExpectedYieldPerHa, now(), @CreatedBy, 1);
                """,
                new
                {
                    season.Id,
                    season.TenantId,
                    season.FarmId,
                    season.Name,
                    season.Crop,
                    season.StartDate,
                    season.EndDate,
                    Status = (short)SeasonStatus.Planned,
                    season.PlannedAreaHa,
                    season.ExpectedYieldPerHa,
                    CreatedBy = tenantContext.UserId
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var dto = ToDto(season, 1);
            await connection.WriteAuditAsync(
                transaction,
                tenantContext,
                "create",
                "Season",
                season.Id,
                null,
                dto,
                cancellationToken).ConfigureAwait(false);
            await connection.EnqueueAsync(
                transaction,
                tenantContext.TenantId,
                "SeasonCreated",
                season.Id,
                dto,
                cancellationToken).ConfigureAwait(false);
            return dto;
        }, cancellationToken);
    }

    public Task<FieldOperationResult> RegisterPlantingAsync(
        RegisterPlantingCommand command,
        CancellationToken cancellationToken) =>
        database.InTenantTransactionAsync(
            (connection, transaction) => RegisterPlantingInternalAsync(connection, transaction, command, cancellationToken),
            cancellationToken);

    public Task<FieldOperationResult> RegisterHarvestAsync(
        RegisterHarvestCommand command,
        CancellationToken cancellationToken) =>
        database.InTenantTransactionAsync(
            (connection, transaction) => RegisterHarvestInternalAsync(connection, transaction, command, cancellationToken),
            cancellationToken);

    public Task<PagedResult<SeasonDto>> ListSeasonsAsync(
        Guid? farmId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        return database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
                """
                select count(*) from agro360.agriculture_seasons
                where tenant_id = @TenantId and deleted_at is null
                  and (@FarmId is null or farm_id = @FarmId);

                select id, farm_id as FarmId, name, crop, start_date as StartDate,
                       end_date as EndDate,
                       case status when 1 then 'PLANNED' when 2 then 'ACTIVE'
                           when 3 then 'HARVESTED' when 4 then 'CLOSED' else 'CANCELLED' end as Status,
                       planned_area_ha as PlannedAreaHa,
                       expected_yield_per_ha as ExpectedYieldPerHa,
                       version
                from agro360.agriculture_seasons
                where tenant_id = @TenantId and deleted_at is null
                  and (@FarmId is null or farm_id = @FarmId)
                order by start_date desc, name
                limit @PageSize offset @Offset;
                """,
                new
                {
                    tenantContext.TenantId,
                    FarmId = farmId,
                    PageSize = pageSize,
                    Offset = (page - 1) * pageSize
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            var total = await grid.ReadSingleAsync<long>().ConfigureAwait(false);
            var items = (await grid.ReadAsync<SeasonDto>().ConfigureAwait(false)).ToArray();
            return new PagedResult<SeasonDto>(items, page, pageSize, total);
        }, cancellationToken);
    }

    private async Task<FieldOperationResult> RegisterPlantingInternalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        RegisterPlantingCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await FindOperationByIdempotencyAsync(
            connection,
            transaction,
            command.IdempotencyKey,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var areaHa = Guard.Positive(command.AreaHa, nameof(command.AreaHa));
        var seedQuantity = Guard.Positive(command.SeedQuantity, nameof(command.SeedQuantity));
        var seedUnit = Guard.Required(command.SeedUnit, nameof(command.SeedUnit), 16).ToLowerInvariant();
        var season = await LoadSeasonAndFieldAsync(
            connection,
            transaction,
            command.SeasonId,
            command.FieldId,
            cancellationToken).ConfigureAwait(false);

        if (season.Status is 3 or 4 or 5)
        {
            throw new ConflictException("A safra não aceita novos plantios.", "season.not_open");
        }

        if (areaHa > season.FieldAreaHa)
        {
            throw new DomainException("A área plantada excede a área do talhão.", "agro360.agriculture_area_exceeds_field");
        }

        var overlap = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists(
                select 1
                from agro360.agriculture_field_operations o
                join agro360.agriculture_seasons s on s.id = o.season_id and s.tenant_id = o.tenant_id
                where o.tenant_id = @TenantId
                  and o.field_id = @FieldId
                  and o.operation_type = 'PLANTING'
                  and o.status <> 'CANCELLED'
                  and o.season_id <> @SeasonId
                  and daterange(s.start_date, s.end_date, '[]') && daterange(@StartDate, @EndDate, '[]')
            );
            """,
            new
            {
                tenantContext.TenantId,
                command.FieldId,
                command.SeasonId,
                season.StartDate,
                season.EndDate
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (overlap)
        {
            throw new ConflictException(
                "O talhão já possui plantio incompatível no mesmo período.",
                "agro360.agriculture_field_season_overlap");
        }

        var balance = await LockBalanceAsync(
            connection,
            transaction,
            command.WarehouseId,
            command.SeedProductId,
            cancellationToken).ConfigureAwait(false);
        if (balance is null || balance.Available - balance.Reserved < seedQuantity)
        {
            throw new ConflictException("Estoque de sementes insuficiente.", "agro360.inventory_insufficient_stock");
        }

        if (!string.Equals(balance.Unit, seedUnit, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("A unidade da semente difere do saldo armazenado.", "agro360.inventory_unit_mismatch");
        }

        var operationId = Guid.CreateVersion7();
        var movementId = Guid.CreateVersion7();
        var costEntryId = Guid.CreateVersion7();
        var costAmount = decimal.Round(seedQuantity * balance.AverageCost, 4);
        var newBalance = balance.Available - seedQuantity;
        var newBalanceVersion = balance.Version + 1;

        await UpdateBalanceAsync(
            connection,
            transaction,
            balance,
            newBalance,
            balance.AverageCost,
            newBalanceVersion,
            cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.inventory_stock_movements
                (id, tenant_id, warehouse_id, product_id, movement_type, quantity, unit,
                 unit_cost, total_cost, reference_type, reference_id, notes, idempotency_key,
                 balance_after, average_cost_after, balance_version, occurred_at, created_by)
            values
                (@MovementId, @TenantId, @WarehouseId, @ProductId, 'CONSUMPTION', @Quantity, @Unit,
                 @UnitCost, @TotalCost, 'FIELD_OPERATION', @OperationId, @Notes, @MovementIdempotencyKey,
                 @BalanceAfter, @AverageCost, @BalanceVersion, @ExecutedAt, @CreatedBy);

            insert into agro360.agriculture_field_operations
                (id, tenant_id, farm_id, field_id, season_id, operation_type, status,
                 area_ha, quantity, unit, executed_at, notes, idempotency_key, created_at, created_by, version)
            values
                (@OperationId, @TenantId, @FarmId, @FieldId, @SeasonId, 'PLANTING', 'COMPLETED',
                 @AreaHa, @Quantity, @Unit, @ExecutedAt, @Notes, @IdempotencyKey, now(), @CreatedBy, 1);

            insert into agro360.cost_entries
                (id, tenant_id, farm_id, season_id, field_id, source_type, source_id,
                 category, amount, currency, occurred_on, created_at, created_by)
            values
                (@CostEntryId, @TenantId, @FarmId, @SeasonId, @FieldId, 'STOCK_MOVEMENT', @MovementId,
                 'SEEDS', @TotalCost, 'BRL', cast(@ExecutedAt as date), now(), @CreatedBy);

            update agro360.agriculture_seasons
            set status = 2, updated_at = now(), updated_by = @CreatedBy, version = version + 1
            where id = @SeasonId and tenant_id = @TenantId;
            """,
            new
            {
                MovementId = movementId,
                tenantContext.TenantId,
                command.WarehouseId,
                ProductId = command.SeedProductId,
                Quantity = seedQuantity,
                Unit = seedUnit,
                UnitCost = balance.AverageCost,
                TotalCost = costAmount,
                OperationId = operationId,
                command.Notes,
                MovementIdempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
                    ? null
                    : $"stock:{command.IdempotencyKey}",
                BalanceAfter = newBalance,
                AverageCost = balance.AverageCost,
                BalanceVersion = newBalanceVersion,
                command.ExecutedAt,
                CreatedBy = tenantContext.UserId,
                season.FarmId,
                command.FieldId,
                command.SeasonId,
                AreaHa = areaHa,
                command.IdempotencyKey,
                CostEntryId = costEntryId
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        await LinkPlantingTraceabilityAsync(
            connection,
            transaction,
            operationId,
            command.SeasonId,
            command.FieldId,
            command.SeedProductId,
            season,
            cancellationToken).ConfigureAwait(false);

        var result = new FieldOperationResult(operationId, costEntryId, movementId, costAmount, "COMPLETED");
        await connection.WriteAuditAsync(
            transaction,
            tenantContext,
            "plant",
            "FieldOperation",
            operationId,
            null,
            result,
            cancellationToken).ConfigureAwait(false);
        await connection.EnqueueAsync(
            transaction,
            tenantContext.TenantId,
            "CropPlanted",
            operationId,
            result,
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<FieldOperationResult> RegisterHarvestInternalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        RegisterHarvestCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await FindOperationByIdempotencyAsync(
            connection,
            transaction,
            command.IdempotencyKey,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var quantity = Guard.Positive(command.Quantity, nameof(command.Quantity));
        var unit = Guard.Required(command.Unit, nameof(command.Unit), 16).ToLowerInvariant();
        var season = await LoadSeasonAndFieldAsync(
            connection,
            transaction,
            command.SeasonId,
            command.FieldId,
            cancellationToken).ConfigureAwait(false);
        if (season.Status != 2)
        {
            throw new ConflictException("A colheita exige uma safra ativa.", "season.not_active");
        }

        var productUnit = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            select base_unit from agro360.inventory_products
            where id = @ProductId and tenant_id = @TenantId and deleted_at is null;
            """,
            new { ProductId = command.HarvestedProductId, tenantContext.TenantId },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false)
            ?? throw new NotFoundException("Produto colhido", command.HarvestedProductId);
        if (!string.Equals(productUnit, unit, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("A unidade colhida difere da unidade base do produto.", "agro360.inventory_unit_mismatch");
        }

        var costSummary = await connection.QuerySingleAsync<HarvestCostRow>(new CommandDefinition(
            """
            select
                (select coalesce(sum(c.amount), 0)
                 from agro360.cost_entries c
                 where c.tenant_id = @TenantId and c.season_id = @SeasonId) as TotalCost,
                (select coalesce(sum(m.total_cost), 0)
                 from agro360.inventory_stock_movements m
                 join agro360.agriculture_field_operations o
                   on o.id = m.reference_id and o.tenant_id = m.tenant_id
                 where m.tenant_id = @TenantId
                   and o.season_id = @SeasonId
                   and m.movement_type = 'PRODUCTION') as AlreadyAllocated;
            """,
            new { tenantContext.TenantId, command.SeasonId },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        var allocatedCost = Math.Max(0, costSummary.TotalCost - costSummary.AlreadyAllocated);
        var unitCost = decimal.Round(allocatedCost / quantity, 4);
        var balance = await LockBalanceAsync(
            connection,
            transaction,
            command.DestinationWarehouseId,
            command.HarvestedProductId,
            cancellationToken).ConfigureAwait(false);

        if (balance is null)
        {
            balance = new BalanceRow
            {
                Id = Guid.CreateVersion7(),
                Available = 0,
                Reserved = 0,
                AverageCost = 0,
                Version = 0,
                Unit = unit
            };
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.inventory_stock_balances
                    (id, tenant_id, warehouse_id, product_id, unit, available, reserved, minimum,
                     average_cost, created_at, updated_at, version)
                values
                    (@Id, @TenantId, @WarehouseId, @ProductId, @Unit, 0, 0, 0, 0, now(), now(), 0);
                """,
                new
                {
                    balance.Id,
                    tenantContext.TenantId,
                    WarehouseId = command.DestinationWarehouseId,
                    ProductId = command.HarvestedProductId,
                    Unit = unit
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        var newBalance = balance.Available + quantity;
        var newAverageCost = decimal.Round(
            ((balance.Available * balance.AverageCost) + allocatedCost) / newBalance,
            4);
        var newBalanceVersion = balance.Version + 1;
        await UpdateBalanceAsync(
            connection,
            transaction,
            balance,
            newBalance,
            newAverageCost,
            newBalanceVersion,
            cancellationToken).ConfigureAwait(false);

        var operationId = Guid.CreateVersion7();
        var movementId = Guid.CreateVersion7();
        var costEntryId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.inventory_stock_movements
                (id, tenant_id, warehouse_id, product_id, movement_type, quantity, unit,
                 unit_cost, total_cost, lot_number, reference_type, reference_id, idempotency_key,
                 balance_after, average_cost_after, balance_version, occurred_at, created_by)
            values
                (@MovementId, @TenantId, @WarehouseId, @ProductId, 'PRODUCTION', @Quantity, @Unit,
                 @UnitCost, @AllocatedCost, @LotNumber, 'FIELD_OPERATION', @OperationId, @MovementIdempotencyKey,
                 @BalanceAfter, @AverageCostAfter, @BalanceVersion, @ExecutedAt, @CreatedBy);

            insert into agro360.agriculture_field_operations
                (id, tenant_id, farm_id, field_id, season_id, operation_type, status,
                 quantity, unit, executed_at, idempotency_key, created_at, created_by, version)
            values
                (@OperationId, @TenantId, @FarmId, @FieldId, @SeasonId, 'HARVEST', 'COMPLETED',
                 @Quantity, @Unit, @ExecutedAt, @IdempotencyKey, now(), @CreatedBy, 1);

            insert into agro360.cost_entries
                (id, tenant_id, farm_id, season_id, field_id, source_type, source_id,
                 category, amount, currency, occurred_on, created_at, created_by)
            values
                (@CostEntryId, @TenantId, @FarmId, @SeasonId, @FieldId, 'FIELD_OPERATION', @OperationId,
                 'HARVEST', 0, 'BRL', cast(@ExecutedAt as date), now(), @CreatedBy);
            """,
            new
            {
                MovementId = movementId,
                tenantContext.TenantId,
                WarehouseId = command.DestinationWarehouseId,
                ProductId = command.HarvestedProductId,
                Quantity = quantity,
                Unit = unit,
                UnitCost = unitCost,
                AllocatedCost = allocatedCost,
                command.LotNumber,
                OperationId = operationId,
                MovementIdempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
                    ? null
                    : $"stock:{command.IdempotencyKey}",
                BalanceAfter = newBalance,
                AverageCostAfter = newAverageCost,
                BalanceVersion = newBalanceVersion,
                command.ExecutedAt,
                CreatedBy = tenantContext.UserId,
                season.FarmId,
                command.FieldId,
                command.SeasonId,
                command.IdempotencyKey,
                CostEntryId = costEntryId
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        await LinkHarvestTraceabilityAsync(
            connection,
            transaction,
            operationId,
            movementId,
            command,
            season,
            cancellationToken).ConfigureAwait(false);
        var result = new FieldOperationResult(operationId, costEntryId, movementId, 0, "COMPLETED");
        await connection.WriteAuditAsync(
            transaction,
            tenantContext,
            "harvest",
            "FieldOperation",
            operationId,
            null,
            result,
            cancellationToken).ConfigureAwait(false);
        await connection.EnqueueAsync(
            transaction,
            tenantContext.TenantId,
            "CropHarvested",
            operationId,
            new { result, Quantity = quantity, Unit = unit, AllocatedCost = allocatedCost },
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<SeasonFieldRow> LoadSeasonAndFieldAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid seasonId,
        Guid fieldId,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<SeasonFieldRow>(new CommandDefinition(
            """
            select s.id as SeasonId, s.farm_id as FarmId, s.name as SeasonName, s.crop,
                   s.start_date as StartDate, s.end_date as EndDate, s.status,
                   f.id as FieldId, f.name as FieldName, f.area_ha as FieldAreaHa
            from agro360.agriculture_seasons s
            join agro360.geo_fields f on f.id = @FieldId and f.farm_id = s.farm_id and f.tenant_id = s.tenant_id
            where s.id = @SeasonId and s.tenant_id = @TenantId
              and s.deleted_at is null and f.deleted_at is null
            for update of s;
            """,
            new { SeasonId = seasonId, FieldId = fieldId, tenantContext.TenantId },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false)
        ?? throw new ConflictException("Safra e talhão não pertencem à mesma fazenda.", "agro360.agriculture_season_field_mismatch");

    private Task<BalanceRow?> LockBalanceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid warehouseId,
        Guid productId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<BalanceRow>(new CommandDefinition(
            """
            select id, unit, available, reserved, average_cost as AverageCost, version
            from agro360.inventory_stock_balances
            where tenant_id = @TenantId and warehouse_id = @WarehouseId and product_id = @ProductId
            for update;
            """,
            new { tenantContext.TenantId, WarehouseId = warehouseId, ProductId = productId },
            transaction,
            cancellationToken: cancellationToken));

    private async Task UpdateBalanceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        BalanceRow balance,
        decimal available,
        decimal averageCost,
        long version,
        CancellationToken cancellationToken)
    {
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            update agro360.inventory_stock_balances
            set available = @Available, average_cost = @AverageCost,
                updated_at = now(), version = @Version
            where id = @Id and tenant_id = @TenantId and version = @ExpectedVersion;
            """,
            new
            {
                balance.Id,
                tenantContext.TenantId,
                Available = available,
                AverageCost = averageCost,
                Version = version,
                ExpectedVersion = balance.Version
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (affected != 1)
        {
            throw new ConflictException("O saldo foi alterado por outra operação.");
        }
    }

    private Task<FieldOperationResult?> FindOperationByIdempotencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Task.FromResult<FieldOperationResult?>(null);
        }

        return connection.QuerySingleOrDefaultAsync<FieldOperationResult>(new CommandDefinition(
            """
            select o.id as OperationId, c.id as CostEntryId, m.id as StockMovementId,
                   c.amount as CostAmount, o.status
            from agro360.agriculture_field_operations o
            join agro360.inventory_stock_movements m on m.reference_id = o.id and m.tenant_id = o.tenant_id
            join agro360.cost_entries c
              on c.tenant_id = o.tenant_id
             and (c.source_id = o.id or c.source_id = m.id)
            where o.tenant_id = @TenantId and o.idempotency_key = @IdempotencyKey;
            """,
            new { tenantContext.TenantId, IdempotencyKey = idempotencyKey },
            transaction,
            cancellationToken: cancellationToken));
    }

    private async Task LinkPlantingTraceabilityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        Guid seasonId,
        Guid fieldId,
        Guid productId,
        SeasonFieldRow row,
        CancellationToken cancellationToken)
    {
        var operationNode = await UpsertNodeAsync(connection, transaction, "FIELD_OPERATION", operationId, "Plantio", cancellationToken).ConfigureAwait(false);
        var fieldNode = await UpsertNodeAsync(connection, transaction, "FIELD", fieldId, row.FieldName, cancellationToken).ConfigureAwait(false);
        var seasonNode = await UpsertNodeAsync(connection, transaction, "SEASON", seasonId, row.SeasonName, cancellationToken).ConfigureAwait(false);
        var productNode = await UpsertNodeAsync(connection, transaction, "PRODUCT", productId, "Insumo aplicado", cancellationToken).ConfigureAwait(false);
        await InsertEdgesAsync(
            connection,
            transaction,
            [(productNode, operationNode, "CONSUMED_BY"), (fieldNode, operationNode, "HOSTED"), (operationNode, seasonNode, "APPLIED_TO")],
            cancellationToken).ConfigureAwait(false);
    }

    private async Task LinkHarvestTraceabilityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        Guid movementId,
        RegisterHarvestCommand command,
        SeasonFieldRow row,
        CancellationToken cancellationToken)
    {
        var operationNode = await UpsertNodeAsync(connection, transaction, "FIELD_OPERATION", operationId, "Colheita", cancellationToken).ConfigureAwait(false);
        var seasonNode = await UpsertNodeAsync(connection, transaction, "SEASON", command.SeasonId, row.SeasonName, cancellationToken).ConfigureAwait(false);
        var productNode = await UpsertNodeAsync(connection, transaction, "PRODUCT", command.HarvestedProductId, row.Crop, cancellationToken).ConfigureAwait(false);
        var movementNode = await UpsertNodeAsync(connection, transaction, "STOCK_MOVEMENT", movementId, "Entrada da colheita", cancellationToken).ConfigureAwait(false);
        await InsertEdgesAsync(
            connection,
            transaction,
            [(seasonNode, operationNode, "HARVESTED_BY"), (operationNode, productNode, "PRODUCED"), (productNode, movementNode, "STORED_AS")],
            cancellationToken).ConfigureAwait(false);
    }

    private Task<Guid> UpsertNodeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string entityType,
        Guid entityId,
        string label,
        CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            insert into agro360.traceability_nodes
                (id, tenant_id, entity_type, entity_id, label, created_at)
            values
                (@Id, @TenantId, @EntityType, @EntityId, @Label, now())
            on conflict (tenant_id, entity_type, entity_id)
            do update set label = excluded.label
            returning id;
            """,
            new
            {
                Id = Guid.CreateVersion7(),
                tenantContext.TenantId,
                EntityType = entityType,
                EntityId = entityId,
                Label = label
            },
            transaction,
            cancellationToken: cancellationToken));

    private async Task InsertEdgesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyCollection<(Guid From, Guid To, string Relation)> edges,
        CancellationToken cancellationToken)
    {
        foreach (var edge in edges)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.traceability_edges
                    (id, tenant_id, from_node_id, to_node_id, relation_type, created_at)
                values
                    (@Id, @TenantId, @From, @To, @Relation, now())
                on conflict (tenant_id, from_node_id, to_node_id, relation_type) do nothing;
                """,
                new
                {
                    Id = Guid.CreateVersion7(),
                    tenantContext.TenantId,
                    edge.From,
                    edge.To,
                    edge.Relation
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    private static SeasonDto ToDto(Season season, long version) => new(
        season.Id,
        season.FarmId,
        season.Name,
        season.Crop,
        season.StartDate,
        season.EndDate,
        season.Status.ToString().ToUpperInvariant(),
        season.PlannedAreaHa,
        season.ExpectedYieldPerHa,
        version);

    private sealed class SeasonFieldRow
    {
        public Guid SeasonId { get; init; }

        public Guid FarmId { get; init; }

        public string SeasonName { get; init; } = string.Empty;

        public string Crop { get; init; } = string.Empty;

        public DateOnly StartDate { get; init; }

        public DateOnly EndDate { get; init; }

        public short Status { get; init; }

        public Guid FieldId { get; init; }

        public string FieldName { get; init; } = string.Empty;

        public decimal FieldAreaHa { get; init; }
    }

    private sealed class BalanceRow
    {
        public Guid Id { get; init; }

        public string Unit { get; init; } = string.Empty;

        public decimal Available { get; init; }

        public decimal Reserved { get; init; }

        public decimal AverageCost { get; init; }

        public long Version { get; init; }
    }

    private sealed class HarvestCostRow
    {
        public decimal TotalCost { get; init; }

        public decimal AlreadyAllocated { get; init; }
    }
}
