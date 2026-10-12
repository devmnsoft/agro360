using System.Security.Cryptography;
using System.Text;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agro360.Infrastructure.Services;

public sealed class InspectionIntentsOptions
{
    public const string SectionName = "InspectionIntents";

    /// <summary>Máximo de tentativas de criação de run (falha técnica) antes de dead-letter.</summary>
    public int MaximumAttempts { get; init; } = 10;

    /// <summary>Ciclo de reavaliação quando falta modelo: pendência de configuração, sem loop apertado.</summary>
    public int ModelMissingPollSeconds { get; init; } = 1800;

    /// <summary>Máximo de reavaliações em falta de modelo antes de dead-letter com motivo.</summary>
    public int ModelMissingMaximumAttempts { get; init; } = 336;

    /// <summary>Intervalo de varredura do worker (segundos).</summary>
    public int PollSeconds { get; init; } = 15;

    /// <summary>Intents reclamados por ciclo e por tenant.</summary>
    public int BatchSize { get; init; } = 20;

    /// <summary>Lease de processamento: crash durante o trabalho permite reclaim após expirar.</summary>
    public int LeaseSeconds { get; init; } = 300;

    /// <summary>Janela de recuperação de origens confirmadas sem intent (dias).</summary>
    public int OriginSweepWindowDays { get; init; } = 7;
}

/// <summary>Chaves e hash canônicos dos eventos operacionais de inspeção (única fonte).</summary>
public static class InspectionEventKeys
{
    public static string BuildKey(string processCode, Guid originId, string? customKey = null) =>
        string.IsNullOrWhiteSpace(customKey)
            ? $"EVT:{processCode.Trim().ToUpperInvariant()}:{originId:N}"
            : customKey.Trim();

    public static string ComputeHash(string processCode, string originType, Guid originId, Guid? productId, Guid? lotId, Guid? unitId)
    {
        var raw = $"{processCode}|{originType}|{originId:N}|{productId?.ToString("N")}|{lotId?.ToString("N")}|{unitId?.ToString("N")}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Backoff exponencial limitado a 1 hora — mesmas semânticas do outbox canônico.</summary>
    public static int BackoffSeconds(int attempts) => (int)Math.Min(3600, Math.Pow(2, Math.Min(attempts, 10)));
}

/// <summary>
/// Máquina de estados única para intents de inspeção por evento. Usada tanto pelo disparo inline
/// (pós-commit do comando original) quanto pelo worker de recuperação e pelo re-disparo manual.
/// Requer ITenantContext estabelecido (middleware HTTP ou SetScope no worker).
/// </summary>
public interface IInspectionIntentProcessor
{
    Task<OperationalInspectionEventResult> ProcessIntentAsync(Guid intentId, CancellationToken ct);
}

public sealed partial class InspectionIntentProcessor(
    DatabaseExecutor db,
    ITenantContext tenant,
    IInspectionService inspectionService,
    IOptions<InspectionIntentsOptions> options,
    ILogger<InspectionIntentProcessor> logger) : IInspectionIntentProcessor
{
    private const string ResolvedNote = "Origem saiu do estado pendente sem nova ação necessária.";
    private const string SkippedNote = "Nenhum operador vinculado ao evento para executar a inspeção automática.";

    public async Task<OperationalInspectionEventResult> ProcessIntentAsync(Guid intentId, CancellationToken ct)
    {
        var row = await db.InTenantTransactionAsync<IntentWorkRow?>(async (c, t) =>
        {
            var loaded = await c.QuerySingleOrDefaultAsync<IntentWorkRow>(new CommandDefinition("""
                select id Id, process_code ProcessCode, origin_type OriginType, origin_id OriginId,
                       product_id ProductId, lot_id LotId, unit_id UnitId, actor_id ActorId,
                       idempotency_key IdempotencyKey, status Status, run_id RunId, notes Notes,
                       attempts Attempts, error_code ErrorCode
                from agro360.quality_inspection_event_intents
                where tenant_id = @TenantId and id = @Id and deleted_at is null
                for update
                """, new { tenant.TenantId, Id = intentId }, t, cancellationToken: ct));
            if (loaded is null)
                throw new NotFoundException("Intent de inspeção", intentId);

            // Estados terminais: devolver sem reprocessar (idempotência do processamento).
            if (loaded.Status is "STARTED" or "AMBIGUOUS" or "SKIPPED_NO_ACTOR" or "RESOLVED" or "DEAD_LETTERED")
                return loaded;

            // Revalidação da origem (tenant garantido por RLS + @TenantId): o trabalho só é retomado
            // se a origem continuar existindo; se ela já saiu do estado pendente, adota run existente.
            var originState = await c.QuerySingleAsync<string>(new CommandDefinition(OriginValidationSql(loaded.OriginType),
                new { tenant.TenantId, OriginId = loaded.OriginId }, t, cancellationToken: ct));
            loaded.OriginState = originState;
            return loaded;
        }, ct);

        if (row is null)
            throw new NotFoundException("Intent de inspeção", intentId); // defensivo: a ausência já é tratada na transação

        if (row.OriginState == "MISSING")
        {
            LogOriginMissing(logger, tenant.TenantId, row.OriginType, row.OriginId, row.Attempts);
            await WriteOutcomeAsync(row, outcome: new(
                Status: row.Attempts + 1 >= options.Value.MaximumAttempts ? "DEAD_LETTERED" : "PENDING",
                ErrorCode: "inspection.origin_missing",
                LastError: $"Origem ({row.OriginType}) não encontrada no momento da recuperação.",
                AttemptsAdd: 1,
                BackoffSql: true,
                ClearClaim: true,
                DeadLetter: row.Attempts + 1 >= options.Value.MaximumAttempts), ct);
            return Result(row, row.Attempts + 1 >= options.Value.MaximumAttempts ? "DEAD_LETTERED" : "PENDING", null,
                "Origem não encontrada; tentativas registradas.");
        }

        if (row.OriginState == "SETTLED")
        {
            // Origem confirmada, mas já não pendente: se existe run pela chave idempotente, adota-o.
            var existingRun = await db.InTenantTransactionAsync<Guid?>(async (c, t) =>
                await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
                    select id from agro360.quality_inspection_runs
                    where tenant_id = @TenantId and idempotency_key = @Key and deleted_at is null
                    """, new { tenant.TenantId, Key = row.IdempotencyKey }, t, cancellationToken: ct)), ct);

            if (existingRun.HasValue)
            {
                await WriteOutcomeAsync(row, new("STARTED", RunId: existingRun.Value,
                    Notes: "Inspeção recuperada: run existente adota o intent.", ClearClaim: true, ResetClock: true), ct);
                return Result(row, "STARTED", existingRun.Value, "Inspeção recuperada a partir de run existente.");
            }

            await WriteOutcomeAsync(row, new("RESOLVED", Notes: ResolvedNote, ClearClaim: true), ct);
            return Result(row, "RESOLVED", null, ResolvedNote);
        }

        // PENDING efetivo: tenta criar o run com o ator original do evento.
        if (row.ActorId is null)
        {
            await WriteOutcomeAsync(row, new("SKIPPED_NO_ACTOR", Notes: SkippedNote, ClearClaim: true), ct);
            return Result(row, "SKIPPED_NO_ACTOR", null, SkippedNote);
        }

        try
        {
            var startCommand = new InspectionStartRunCommand(
                ModelVersionId: null,
                ProcessCode: row.ProcessCode,
                OriginType: row.OriginType,
                OriginId: row.OriginId,
                ProductId: row.ProductId,
                LotId: row.LotId,
                UnitId: row.UnitId,
                InspectorId: row.ActorId.Value,
                SelectionMode: "AUTOMATIC",
                IdempotencyKey: row.IdempotencyKey);

            var runId = await inspectionService.StartRunAsync(startCommand, ct);
            await WriteOutcomeAsync(row, new("STARTED", RunId: runId,
                Notes: "Inspeção iniciada a partir do evento de origem.", ClearClaim: true, ResetClock: true), ct);
            return Result(row, "STARTED", runId, "Inspeção iniciada a partir do evento de origem.");
        }
        catch (ConflictException cex) when (cex.Code == "inspection.ambiguous_model")
        {
            await WriteOutcomeAsync(row, new("AMBIGUOUS", Notes: cex.Message,
                ErrorCode: "inspection.ambiguous_model", ClearClaim: true), ct);
            return Result(row, "AMBIGUOUS", null, cex.Message);
        }
        catch (ConflictException cex) when (cex.Code == "persistence.unique_violation")
        {
            // Concorrência na criação do run: quem perdeu a corrida adota o run vencedor pela chave.
            var winningRun = await db.InTenantTransactionAsync<Guid?>(async (c, t) =>
                await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
                    select id from agro360.quality_inspection_runs
                    where tenant_id = @TenantId and idempotency_key = @Key and deleted_at is null
                    """, new { tenant.TenantId, Key = row.IdempotencyKey }, t, cancellationToken: ct)), ct);
            if (winningRun.HasValue)
            {
                await WriteOutcomeAsync(row, new("STARTED", RunId: winningRun.Value,
                    Notes: "Inspeção iniciada por requisição concorrente; intent adotou o run existente.", ClearClaim: true, ResetClock: true), ct);
                return Result(row, "STARTED", winningRun.Value, "Concorrência resolvida: run existente adotado.");
            }
            await WriteTransientFailureAsync(row, cex, ct);
            return Result(row, "PENDING", null, "Falha técnica registrada; nova tentativa agendada.");
        }
        catch (DomainException dex) when (dex.Code == "inspection.inspector_inactive")
        {
            // Falha permanente conhecida: ator inexistente/inativo. Motivo + ação (re-disparo com novo ator).
            await WriteOutcomeAsync(row, new("DEAD_LETTERED", Notes: dex.Message,
                ErrorCode: "inspection.inspector_inactive", LastError: dex.Message, ClearClaim: true, DeadLetter: true), ct);
            return Result(row, "DEAD_LETTERED", null, dex.Message);
        }
        catch (InvalidOperationException iex)
        {
            // Ausência de modelo = pendência de configuração: ciclo longo, sem loop apertado.
            var attempts = row.Attempts + 1;
            var exhausted = attempts >= options.Value.ModelMissingMaximumAttempts;
            await WriteOutcomeAsync(row, new(exhausted ? "DEAD_LETTERED" : "PENDING_MODEL", Notes: iex.Message,
                ErrorCode: exhausted ? "inspection.model_missing" : null,
                LastError: iex.Message,
                AttemptsAdd: 1,
                ModelMissingDelaySeconds: options.Value.ModelMissingPollSeconds,
                DeadLetter: exhausted,
                ClearClaim: true), ct);
            return Result(row, exhausted ? "DEAD_LETTERED" : "PENDING_MODEL", null, iex.Message);
        }
        catch (Exception ex)
        {
            LogProcessingFailure(logger, tenant.TenantId, row.IdempotencyKey, row.Attempts + 1, ex);
            await WriteTransientFailureAsync(row, ex, ct);
            return Result(row, "PENDING", null, $"Falha técnica: {ex.Message}");
        }
    }

    private async Task WriteTransientFailureAsync(IntentWorkRow row, Exception failure, CancellationToken ct)
    {
        var attempts = row.Attempts + 1;
        var exhausted = attempts >= options.Value.MaximumAttempts;
        await WriteOutcomeAsync(row, new(exhausted ? "DEAD_LETTERED" : "PENDING",
            Notes: $"Falha técnica: {failure.Message}",
            ErrorCode: "inspection.creation_failed",
            LastError: $"{failure.GetType().Name}: {failure.Message}",
            AttemptsAdd: 1,
            BackoffSql: true,
            DeadLetter: exhausted,
            ClearClaim: true), ct);
    }

    private Task WriteOutcomeAsync(IntentWorkRow row, Outcome outcome, CancellationToken ct) =>
        db.InTenantTransactionAsync(async (c, t) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                update agro360.quality_inspection_event_intents
                set status = @Status,
                    run_id = coalesce(@RunId, run_id),
                    notes = case
                        when @Notes is null then notes
                        when notes is null or notes = '' then @Notes
                        else notes || ' | ' || @Notes
                    end,
                    error_code = @ErrorCode,
                    last_error = @LastError,
                    attempts = attempts + @AttemptsAdd,
                    next_attempt_at = case
                        when @ResetClock then now()
                        when @BackoffSql then now() + make_interval(secs => least(3600, power(2, least(attempts, 10))::int))
                        when @ModelMissingDelaySeconds > 0 then now() + make_interval(secs => @ModelMissingDelaySeconds)
                        else next_attempt_at
                    end,
                    dead_lettered_at = case when @DeadLetter then now() else dead_lettered_at end,
                    claimed_at = case when @ClearClaim then null else claimed_at end,
                    claimed_by = case when @ClearClaim then null else claimed_by end,
                    lease_expires_at = case when @ClearClaim then null else lease_expires_at end,
                    updated_at = now()
                where tenant_id = @TenantId and id = @Id and deleted_at is null
                """, new
                {
                    tenant.TenantId,
                    Id = row.Id,
                    outcome.Status,
                    outcome.RunId,
                    outcome.Notes,
                    outcome.ErrorCode,
                    outcome.LastError,
                    outcome.AttemptsAdd,
                    outcome.ResetClock,
                    outcome.BackoffSql,
                    outcome.ModelMissingDelaySeconds,
                    outcome.DeadLetter,
                    outcome.ClearClaim
                }, t, cancellationToken: ct));
        }, ct);

    private static OperationalInspectionEventResult Result(IntentWorkRow row, string status, Guid? runId, string? message) =>
        new(row.Id, row.ProcessCode, row.OriginType, row.OriginId, status, runId, message);

    /// <summary>
    /// Validação da origem por tipo (fixed internal types — sem interpolação dinâmica de tabela):
    /// MISSING (não existe/excluída), SETTLED (existe mas saiu do estado pendente) ou PENDING.
    /// Tipos sem emissor conhecido ou sem coluna de qualidade são tratados como PENDING (melhor esforço).
    /// </summary>
    private static string OriginValidationSql(string originType) => originType switch
    {
        // production_receipts não possui soft-delete (hard delete + quality_status).
        "production_receipts" => """
            select case
                when not exists (select 1 from agro360.production_receipts q
                                  where q.tenant_id = @TenantId and q.id = @OriginId)
                    then 'MISSING'
                when exists (select 1 from agro360.production_receipts q
                             where q.tenant_id = @TenantId and q.id = @OriginId
                               and q.quality_status = 'AWAITING_INSPECTION')
                    then 'PENDING'
                else 'SETTLED'
             end state
            """,
        "production_batches" => """
            select case
                when not exists (select 1 from agro360.production_batches b
                                  where b.tenant_id = @TenantId and b.id = @OriginId and b.deleted_at is null)
                    then 'MISSING'
                when exists (select 1 from agro360.production_batches b
                             where b.tenant_id = @TenantId and b.id = @OriginId and b.deleted_at is null
                               and b.quality_status = 'PENDING')
                    then 'PENDING'
                else 'SETTLED'
             end state
            """,
        // O disparo original nesses fluxos é incondicional na confirmação do recebimento/retorno,
        // então a existência (sem exclusão) já caracteriza origem pendente de recuperação.
        "procurement_receipts" => """
            select case
                when not exists (select 1 from agro360.procurement_receipts r
                                  where r.tenant_id = @TenantId and r.id = @OriginId and r.deleted_at is null)
                    then 'MISSING'
                else 'PENDING'
             end state
            """,
        "fulfillment_return_receipts" => """
            select case
                when not exists (select 1 from agro360.fulfillment_return_receipts x
                                  where x.tenant_id = @TenantId and x.id = @OriginId)
                    then 'MISSING'
                else 'PENDING'
             end state
            """,
        _ => "select 'PENDING' state"
    };

    [LoggerMessage(EventId = 3101, Level = LogLevel.Warning,
        Message = "Intent de inspeção sem origem. Tenant={TenantId} OriginType={OriginType} OriginId={OriginId} Attempts={Attempts}")]
    private static partial void LogOriginMissing(ILogger logger, Guid tenantId, string originType, Guid originId, int attempts);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Warning,
        Message = "Falha ao processar intent de inspeção. Tenant={TenantId} Key={Key} Attempt={Attempt}")]
    private static partial void LogProcessingFailure(ILogger logger, Guid tenantId, string key, int attempt, Exception exception);

    private sealed record Outcome(
        string Status,
        Guid? RunId = null,
        string? Notes = null,
        string? ErrorCode = null,
        string? LastError = null,
        int AttemptsAdd = 0,
        bool BackoffSql = false,
        int ModelMissingDelaySeconds = 0,
        bool DeadLetter = false,
        bool ClearClaim = false,
        bool ResetClock = false);

    // Classe com propriedades (padrão dos DTOs de linha do código): o Dapper materializa por
    // nome de propriedade; registros posicionais com parâmetros nulos/valor padrão não casam
    // com o bind de construtor e lançam erro de materialização em runtime.
    private sealed class IntentWorkRow
    {
        public Guid Id { get; init; }
        public string ProcessCode { get; init; } = string.Empty;
        public string OriginType { get; init; } = string.Empty;
        public Guid OriginId { get; init; }
        public Guid? ProductId { get; init; }
        public Guid? LotId { get; init; }
        public Guid? UnitId { get; init; }
        public Guid? ActorId { get; init; }
        public string IdempotencyKey { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public Guid? RunId { get; init; }
        public string? Notes { get; init; }
        public int Attempts { get; init; }
        public string? ErrorCode { get; init; }

        /// <summary>Estado da origem no momento do processamento (transiente, calculado fora do SELECT).</summary>
        public string OriginState { get; set; } = "PENDING";
    }
}
