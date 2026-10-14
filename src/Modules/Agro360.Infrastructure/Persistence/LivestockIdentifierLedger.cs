using Dapper;
using Npgsql;

namespace Agro360.Infrastructure.Persistence;

/// <summary>
/// Pecuária P1 (2026-10-12): ledger canônico de identificadores do animal
/// (<c>agro360.livestock_identifier_history</c>). Toda abertura ou troca de brinco/RFID passa por
/// este tipo na MESMA transação do evento de origem (registro, troca dedicada ou edição cadastral):
/// linha com <c>valid_until</c> nulo é a identificação vigente; a troca encerra a linha anterior e
/// abre uma nova com o motivo do encerramento (<c>change_reason</c> obrigatório no schema).
/// O único parcial <c>ux_livestock_identifier_current</c> garante no máximo UMA linha aberta por
/// (tenant, tipo, valor); a abertura usa ON CONFLICT para reexecuções não duplicarem.
/// A tabela auxiliar legada <c>livestock_animal_identifiers</c> não recebe mais escrita neste sprint.
/// </summary>
public static class LivestockIdentifierLedger
{
    /// <summary>Abre a linha inicial de identificação na criação do animal (motivo REGISTRATION).</summary>
    public static Task OpenRegistrationAsync(
        NpgsqlConnection c,
        NpgsqlTransaction t,
        Guid tenantId,
        Guid animalId,
        string type,
        string value,
        DateOnly? validFrom,
        Guid userId,
        CancellationToken ct) =>
        c.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.livestock_identifier_history
                (id, tenant_id, animal_id, identifier_type, identifier, valid_from, valid_until, change_reason, created_at, created_by)
            values
                (gen_random_uuid(), @TenantId, @AnimalId, @Type, @Value, coalesce(@From, now()), null, 'REGISTRATION', now(), @UserId)
            on conflict (tenant_id, identifier_type, lower(identifier)) where valid_until is null do nothing
            """,
            new { TenantId = tenantId, AnimalId = animalId, Type = type, Value = value, From = validFrom, UserId = userId },
            t, cancellationToken: ct));

    /// <summary>Encerra a linha aberta do tipo e abre a identificação nova com o motivo informado.</summary>
    public static Task ChangeAsync(
        NpgsqlConnection c,
        NpgsqlTransaction t,
        Guid tenantId,
        Guid animalId,
        string type,
        string newValue,
        string reason,
        Guid userId,
        CancellationToken ct) =>
        c.ExecuteAsync(new CommandDefinition(
            """
            update agro360.livestock_identifier_history
            set valid_until = now()
            where tenant_id=@TenantId and animal_id=@AnimalId and identifier_type=@Type and valid_until is null;
            insert into agro360.livestock_identifier_history
                (id, tenant_id, animal_id, identifier_type, identifier, valid_from, valid_until, change_reason, created_at, created_by)
            values
                (gen_random_uuid(), @TenantId, @AnimalId, @Type, @Value, now(), null, @Reason, now(), @UserId)
            """,
            new { TenantId = tenantId, AnimalId = animalId, Type = type, Value = newValue, Reason = reason, UserId = userId },
            t, cancellationToken: ct));

    /// <summary>Encerra a linha aberta do tipo sem substituto (ex.: remoção do RFID na edição cadastral).</summary>
    public static Task CloseAsync(
        NpgsqlConnection c,
        NpgsqlTransaction t,
        Guid tenantId,
        Guid animalId,
        string type,
        string reason,
        Guid userId,
        CancellationToken ct) =>
        c.ExecuteAsync(new CommandDefinition(
            """
            update agro360.livestock_identifier_history
            set valid_until = now()
            where tenant_id=@TenantId and animal_id=@AnimalId and identifier_type=@Type and valid_until is null
            """,
            new { TenantId = tenantId, AnimalId = animalId, Type = type },
            t, cancellationToken: ct));
}
