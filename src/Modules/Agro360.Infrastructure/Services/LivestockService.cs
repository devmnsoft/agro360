using Agro360.Application.Contracts;
using Agro360.Domain.Livestock;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Npgsql;

namespace Agro360.Infrastructure.Services;

public sealed class LivestockService(DatabaseExecutor database, ITenantContext tenantContext) : ILivestockService
{
    public Task<AnimalDto> RegisterAnimalAsync(RegisterAnimalCommand command, CancellationToken cancellationToken)
    {
        var animal = Animal.Register(
            tenantContext.TenantId,
            command.FarmId,
            command.Tag,
            command.Species,
            command.Sex,
            command.BirthDate);

        return database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.livestock_animals
                    (id, tenant_id, farm_id, herd_id, tag, rfid, species, breed, sex,
                     birth_date, mother_id, father_id, status, created_at, created_by, version)
                values
                    (@Id, @TenantId, @FarmId, @HerdId, @Tag, @Rfid, @Species, @Breed, @Sex,
                     @BirthDate, @MotherId, @FatherId, 1, now(), @CreatedBy, 1);

                insert into agro360.livestock_animal_events
                    (id, tenant_id, animal_id, event_type, occurred_on, data, created_at, created_by)
                values
                    (@EventId, @TenantId, @Id, 'REGISTRATION', @BirthDate,
                     jsonb_build_object('tag', @Tag, 'species', @Species), now(), @CreatedBy);
                """,
                new
                {
                    animal.Id,
                    animal.TenantId,
                    animal.FarmId,
                    command.HerdId,
                    animal.Tag,
                    command.Rfid,
                    animal.Species,
                    Breed = Guard.Required(command.Breed, nameof(command.Breed), 80),
                    animal.Sex,
                    animal.BirthDate,
                    command.MotherId,
                    command.FatherId,
                    CreatedBy = tenantContext.UserId,
                    EventId = Guid.CreateVersion7()
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var dto = new AnimalDto(
                animal.Id,
                animal.FarmId,
                animal.Tag,
                command.Rfid,
                animal.Species,
                command.Breed,
                animal.Sex,
                animal.BirthDate,
                "ACTIVE",
                null,
                null,
                null,
                1);
            await UpsertNodeAsync(connection, transaction, animal.Id, $"Animal {animal.Tag}", cancellationToken).ConfigureAwait(false);
            await connection.WriteAuditAsync(
                transaction,
                tenantContext,
                "register",
                "Animal",
                animal.Id,
                null,
                dto,
                cancellationToken).ConfigureAwait(false);
            await connection.EnqueueAsync(
                transaction,
                tenantContext.TenantId,
                "AnimalRegistered",
                animal.Id,
                dto,
                cancellationToken).ConfigureAwait(false);
            return dto;
        }, cancellationToken);
    }

    public Task<AnimalEventResult> WeighAsync(WeighAnimalCommand command, CancellationToken cancellationToken) =>
        database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            var existing = await FindEventAsync(
                connection,
                transaction,
                command.IdempotencyKey,
                cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }

            var animal = await LockAnimalAsync(
                connection,
                transaction,
                command.AnimalId,
                cancellationToken).ConfigureAwait(false);
            EnsureActive(animal);
            var weight = Guard.Positive(command.WeightKg, nameof(command.WeightKg));
            decimal? dailyGain = null;
            if (animal.CurrentWeightKg.HasValue && animal.LastWeightDate.HasValue)
            {
                var days = command.MeasuredOn.DayNumber - animal.LastWeightDate.Value.DayNumber;
                if (days <= 0)
                {
                    throw new ConflictException("A nova pesagem deve ocorrer após a pesagem anterior.", "agro360.livestock_weight_date_invalid");
                }

                dailyGain = decimal.Round((weight - animal.CurrentWeightKg.Value) / days, 4);
            }

            var eventId = Guid.CreateVersion7();
            var affected = await connection.ExecuteAsync(new CommandDefinition(
                """
                update agro360.livestock_animals
                set current_weight_kg = @WeightKg, last_weight_date = @MeasuredOn,
                    updated_at = now(), updated_by = @UserId, version = version + 1
                where id = @AnimalId and tenant_id = @TenantId and version = @ExpectedVersion;

                insert into agro360.livestock_animal_events
                    (id, tenant_id, animal_id, event_type, occurred_on, data,
                     idempotency_key, created_at, created_by)
                values
                    (@EventId, @TenantId, @AnimalId, 'WEIGHING', @MeasuredOn,
                     jsonb_build_object('weightKg', @WeightKg, 'dailyGainKg', @DailyGainKg, 'notes', @Notes),
                     @IdempotencyKey, now(), @UserId);
                """,
                new
                {
                    WeightKg = weight,
                    command.MeasuredOn,
                    UserId = tenantContext.UserId,
                    command.AnimalId,
                    tenantContext.TenantId,
                    ExpectedVersion = animal.Version,
                    EventId = eventId,
                    DailyGainKg = dailyGain,
                    command.Notes,
                    command.IdempotencyKey
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (affected != 2)
            {
                throw new ConflictException("O animal foi alterado por outra operação.");
            }

            var result = new AnimalEventResult(eventId, command.AnimalId, "WEIGHING", dailyGain, 0);
            await connection.WriteAuditAsync(
                transaction,
                tenantContext,
                "weigh",
                "Animal",
                command.AnimalId,
                new { animal.CurrentWeightKg, animal.LastWeightDate, animal.Version },
                result,
                cancellationToken).ConfigureAwait(false);
            await connection.EnqueueAsync(
                transaction,
                tenantContext.TenantId,
                "AnimalWeighed",
                command.AnimalId,
                result,
                cancellationToken).ConfigureAwait(false);
            return result;
        }, cancellationToken);

    public Task<AnimalEventResult> TreatAsync(TreatAnimalCommand command, CancellationToken cancellationToken) =>
        database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            var existing = await FindEventAsync(
                connection,
                transaction,
                command.IdempotencyKey,
                cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }

            if (command.WithdrawalDays < 0)
            {
                throw new DomainException("O período de carência não pode ser negativo.", "agro360.livestock_withdrawal_invalid");
            }

            var quantity = Guard.Positive(command.Quantity, nameof(command.Quantity));
            var unit = Guard.Required(command.Unit, nameof(command.Unit), 16).ToLowerInvariant();
            var animal = await LockAnimalAsync(connection, transaction, command.AnimalId, cancellationToken).ConfigureAwait(false);
            EnsureActive(animal);
            var balance = await connection.QuerySingleOrDefaultAsync<BalanceRow>(new CommandDefinition(
                """
                select b.id, b.unit, b.available, b.reserved, b.average_cost as AverageCost, b.version,
                       p.requires_lot as RequiresLot
                from agro360.inventory_stock_balances b
                join agro360.inventory_products p on p.id = b.product_id and p.tenant_id = b.tenant_id
                where b.tenant_id = @TenantId and b.warehouse_id = @WarehouseId and b.product_id = @ProductId
                for update of b;
                """,
                new { tenantContext.TenantId, command.WarehouseId, command.ProductId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false)
                ?? throw new ConflictException("Medicamento sem saldo no depósito.", "agro360.inventory_insufficient_stock");

            if (balance.RequiresLot && string.IsNullOrWhiteSpace(command.LotNumber))
            {
                throw new DomainException("O medicamento exige número de lote.", "agro360.inventory_lot_required");
            }

            if (!string.Equals(balance.Unit, unit, StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainException("A unidade informada difere do saldo do produto.", "agro360.inventory_unit_mismatch");
            }

            if (balance.Available - balance.Reserved < quantity)
            {
                throw new ConflictException("Estoque de medicamento insuficiente.", "agro360.inventory_insufficient_stock");
            }

            var eventId = Guid.CreateVersion7();
            var movementId = Guid.CreateVersion7();
            var costEntryId = Guid.CreateVersion7();
            var cost = decimal.Round(quantity * balance.AverageCost, 4);
            var withdrawalUntil = command.AppliedOn.AddDays(command.WithdrawalDays);
            var newBalance = balance.Available - quantity;

            var affected = await connection.ExecuteAsync(new CommandDefinition(
                """
                update agro360.inventory_stock_balances
                set available = @NewBalance, updated_at = now(), version = version + 1
                where id = @BalanceId and tenant_id = @TenantId and version = @BalanceVersion;

                update agro360.livestock_animals
                set withdrawal_until = case
                        when withdrawal_until is null or withdrawal_until < @WithdrawalUntil then @WithdrawalUntil
                        else withdrawal_until end,
                    updated_at = now(), updated_by = @UserId, version = version + 1
                where id = @AnimalId and tenant_id = @TenantId and version = @AnimalVersion;

                insert into agro360.inventory_stock_movements
                    (id, tenant_id, warehouse_id, product_id, movement_type, quantity, unit,
                     unit_cost, total_cost, lot_number, reference_type, reference_id,
                     idempotency_key, balance_after, average_cost_after, balance_version,
                     occurred_at, created_by)
                values
                    (@MovementId, @TenantId, @WarehouseId, @ProductId, 'CONSUMPTION', @Quantity, @Unit,
                     @UnitCost, @Cost, @LotNumber, 'ANIMAL_EVENT', @EventId,
                     @MovementIdempotencyKey, @NewBalance, @UnitCost, @NewBalanceVersion,
                     cast(@AppliedOn as timestamptz), @UserId);

                insert into agro360.livestock_animal_events
                    (id, tenant_id, animal_id, event_type, occurred_on, data, cost_amount,
                     idempotency_key, created_at, created_by)
                values
                    (@EventId, @TenantId, @AnimalId, @TreatmentType, @AppliedOn,
                     jsonb_build_object('productId', @ProductId, 'quantity', @Quantity,
                         'unit', @Unit, 'lotNumber', @LotNumber,
                         'withdrawalUntil', @WithdrawalUntil, 'notes', @Notes),
                     @Cost, @IdempotencyKey, now(), @UserId);

                insert into agro360.cost_entries
                    (id, tenant_id, farm_id, animal_id, source_type, source_id,
                     category, amount, currency, occurred_on, created_at, created_by)
                values
                    (@CostEntryId, @TenantId, @FarmId, @AnimalId, 'ANIMAL_EVENT', @EventId,
                     'ANIMAL_HEALTH', @Cost, 'BRL', @AppliedOn, now(), @UserId);
                """,
                new
                {
                    NewBalance = newBalance,
                    BalanceId = balance.Id,
                    tenantContext.TenantId,
                    BalanceVersion = balance.Version,
                    WithdrawalUntil = withdrawalUntil,
                    UserId = tenantContext.UserId,
                    command.AnimalId,
                    AnimalVersion = animal.Version,
                    MovementId = movementId,
                    command.WarehouseId,
                    command.ProductId,
                    Quantity = quantity,
                    Unit = unit,
                    UnitCost = balance.AverageCost,
                    Cost = cost,
                    command.LotNumber,
                    EventId = eventId,
                    MovementIdempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
                        ? null
                        : $"stock:{command.IdempotencyKey}",
                    NewBalanceVersion = balance.Version + 1,
                    command.AppliedOn,
                    TreatmentType = Guard.Required(command.TreatmentType, nameof(command.TreatmentType), 40).ToUpperInvariant(),
                    command.Notes,
                    command.IdempotencyKey,
                    CostEntryId = costEntryId,
                    animal.FarmId
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (affected != 5)
            {
                throw new ConflictException("Animal ou estoque foi alterado por outra operação.");
            }

            await LinkTreatmentAsync(
                connection,
                transaction,
                command.AnimalId,
                eventId,
                command.ProductId,
                cancellationToken).ConfigureAwait(false);
            var result = new AnimalEventResult(eventId, command.AnimalId, command.TreatmentType.ToUpperInvariant(), null, cost);
            await connection.WriteAuditAsync(
                transaction,
                tenantContext,
                "treat",
                "Animal",
                command.AnimalId,
                new { animal.WithdrawalUntil, animal.Version },
                new { result, WithdrawalUntil = withdrawalUntil },
                cancellationToken).ConfigureAwait(false);
            await connection.EnqueueAsync(
                transaction,
                tenantContext.TenantId,
                "AnimalTreated",
                command.AnimalId,
                new { result, WithdrawalUntil = withdrawalUntil },
                cancellationToken).ConfigureAwait(false);
            return result;
        }, cancellationToken);

    public Task<PagedResult<AnimalDto>> ListAnimalsAsync(
        Guid? farmId,
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
                select count(*) from agro360.livestock_animals
                where tenant_id = @TenantId and deleted_at is null
                  and (@FarmId is null or farm_id = @FarmId)
                  and (@Search is null or tag ilike '%' || @Search || '%' or rfid ilike '%' || @Search || '%');

                select id, farm_id as FarmId, tag, rfid, species, breed, sex,
                       birth_date as BirthDate,
                       case status when 1 then 'ACTIVE' when 2 then 'QUARANTINE'
                           when 3 then 'SOLD' when 4 then 'DEAD' else 'SLAUGHTERED' end as Status,
                       current_weight_kg as CurrentWeightKg, last_weight_date as LastWeightDate,
                       withdrawal_until as WithdrawalUntil, version
                from agro360.livestock_animals
                where tenant_id = @TenantId and deleted_at is null
                  and (@FarmId is null or farm_id = @FarmId)
                  and (@Search is null or tag ilike '%' || @Search || '%' or rfid ilike '%' || @Search || '%')
                order by tag
                limit @PageSize offset @Offset;
                """,
                new
                {
                    tenantContext.TenantId,
                    FarmId = farmId,
                    Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                    PageSize = pageSize,
                    Offset = (page - 1) * pageSize
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            var total = await grid.ReadSingleAsync<long>().ConfigureAwait(false);
            var items = (await grid.ReadAsync<AnimalDto>().ConfigureAwait(false)).ToArray();
            return new PagedResult<AnimalDto>(items, page, pageSize, total);
        }, cancellationToken);
    }

    private async Task<AnimalRow> LockAnimalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid animalId,
        CancellationToken cancellationToken)
    {
        var animal = await connection.QuerySingleOrDefaultAsync<AnimalRow>(new CommandDefinition(
            """
            select id, farm_id as FarmId, status, current_weight_kg as CurrentWeightKg,
                   last_weight_date as LastWeightDate, withdrawal_until as WithdrawalUntil, version
            from agro360.livestock_animals
            where id = @AnimalId and tenant_id = @TenantId and deleted_at is null
            for update;
            """,
            new { AnimalId = animalId, tenantContext.TenantId },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return animal ?? throw new NotFoundException("Animal", animalId);
    }

    private Task<AnimalEventResult?> FindEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Task.FromResult<AnimalEventResult?>(null);
        }

        return connection.QuerySingleOrDefaultAsync<AnimalEventResult>(new CommandDefinition(
            """
            select id as EventId, animal_id as AnimalId, event_type as EventType,
                   cast(data->>'dailyGainKg' as numeric) as DailyGainKg, cost_amount as CostAmount
            from agro360.livestock_animal_events
            where tenant_id = @TenantId and idempotency_key = @IdempotencyKey;
            """,
            new { tenantContext.TenantId, IdempotencyKey = idempotencyKey },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static void EnsureActive(AnimalRow animal)
    {
        if (animal.Status != 1)
        {
            throw new ConflictException("O animal não está ativo para esta operação.", "agro360.livestock_animal_not_active");
        }
    }

    private Task<Guid> UpsertNodeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid animalId,
        string label,
        CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            insert into agro360.traceability_nodes (id, tenant_id, entity_type, entity_id, label, created_at)
            values (@Id, @TenantId, 'ANIMAL', @AnimalId, @Label, now())
            on conflict (tenant_id, entity_type, entity_id)
            do update set label = excluded.label
            returning id;
            """,
            new { Id = Guid.CreateVersion7(), tenantContext.TenantId, AnimalId = animalId, Label = label },
            transaction,
            cancellationToken: cancellationToken));

    private async Task LinkTreatmentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid animalId,
        Guid eventId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var animalNode = await UpsertNodeAsync(connection, transaction, animalId, "Animal", cancellationToken).ConfigureAwait(false);
        var eventNode = await UpsertGenericNodeAsync(connection, transaction, "ANIMAL_EVENT", eventId, "Manejo sanitário", cancellationToken).ConfigureAwait(false);
        var productNode = await UpsertGenericNodeAsync(connection, transaction, "PRODUCT", productId, "Produto sanitário", cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.traceability_edges (id, tenant_id, from_node_id, to_node_id, relation_type, created_at)
            values
                (@Edge1, @TenantId, @ProductNode, @EventNode, 'CONSUMED_BY', now()),
                (@Edge2, @TenantId, @EventNode, @AnimalNode, 'APPLIED_TO', now())
            on conflict (tenant_id, from_node_id, to_node_id, relation_type) do nothing;
            """,
            new
            {
                Edge1 = Guid.CreateVersion7(),
                Edge2 = Guid.CreateVersion7(),
                tenantContext.TenantId,
                ProductNode = productNode,
                EventNode = eventNode,
                AnimalNode = animalNode
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private Task<Guid> UpsertGenericNodeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string type,
        Guid entityId,
        string label,
        CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            insert into agro360.traceability_nodes (id, tenant_id, entity_type, entity_id, label, created_at)
            values (@Id, @TenantId, @Type, @EntityId, @Label, now())
            on conflict (tenant_id, entity_type, entity_id)
            do update set label = excluded.label
            returning id;
            """,
            new { Id = Guid.CreateVersion7(), tenantContext.TenantId, Type = type, EntityId = entityId, Label = label },
            transaction,
            cancellationToken: cancellationToken));

    private sealed class AnimalRow
    {
        public Guid Id { get; init; }

        public Guid FarmId { get; init; }

        public short Status { get; init; }

        public decimal? CurrentWeightKg { get; init; }

        public DateOnly? LastWeightDate { get; init; }

        public DateOnly? WithdrawalUntil { get; init; }

        public long Version { get; init; }
    }

    private sealed class BalanceRow
    {
        public Guid Id { get; init; }

        public string Unit { get; init; } = string.Empty;

        public decimal Available { get; init; }

        public decimal Reserved { get; init; }

        public decimal AverageCost { get; init; }

        public long Version { get; init; }

        public bool RequiresLot { get; init; }
    }
}
