using Agro360.SharedKernel;

namespace Agro360.Domain.Fleet;

public static class FleetRules
{
    public static readonly string[] AssetStatuses = ["AVAILABLE", "OPERATING", "MAINTENANCE", "UNAVAILABLE", "RESERVED", "WRITTEN_OFF", "SOLD", "INACTIVE"];
    public static readonly string[] CadastralStatuses = ["ACTIVE", "INACTIVE", "WRITTEN_OFF", "SOLD"];
    public static readonly string[] MeterKinds = ["ODOMETER", "HOUR_METER", "ENGINE_HOURS"];
    public static readonly string[] WorkOrderStatuses = ["OPEN", "PLANNED", "PENDING_APPROVAL", "IN_PROGRESS", "PAUSED", "WAITING_PART", "WAITING_VENDOR", "INSPECTION", "COMPLETED", "CANCELLED", "REOPENED"];
    public static readonly string[] DuePolicies = ["FIRST_CRITERION", "CALENDAR_FIXED", "METER_FIXED"];
    public static readonly string[] RefuelSources = ["INTERNAL", "EXTERNAL"];

    public static void ValidateAsset(string code, string status, decimal odometer, decimal hourMeter)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Código interno é obrigatório.", "fleet.code_required");
        if (!AssetStatuses.Contains(status, StringComparer.OrdinalIgnoreCase)) throw new DomainException("Status do ativo inválido.", "fleet.status_invalid");
        if (odometer < 0 || hourMeter < 0) throw new DomainException("Odômetro e horímetro não podem ser negativos.", "fleet.meter_negative");
    }

    public static void ValidateMeterChange(decimal current, decimal next, string? justification, bool canOverride)
    {
        if (next < 0) throw new DomainException("Leitura não pode ser negativa.", "fleet.meter_negative");
        if (next < current && (!canOverride || string.IsNullOrWhiteSpace(justification)))
            throw new ConflictException("Redução de leitura exige evento de reinicialização, justificativa e permissão.", "fleet.meter_decrease");
    }

    public static void ValidateReading(decimal value, DateTimeOffset occurredAt, decimal? previousValue, DateTimeOffset? previousAt, decimal? nextValue, DateTimeOffset? nextAt, bool reset)
    {
        if (value < 0) throw new DomainException("Leitura não pode ser negativa.", "fleet.meter_negative");
        if (reset) return;
        if (previousAt is { } prevAt && occurredAt >= prevAt && previousValue is { } prev && value < prev)
            throw new ConflictException("Leitura retroativa menor que a anterior. Use reinicialização explícita se o medidor virou.", "fleet.meter_retroactive");
        if (nextAt is { } laterAt && occurredAt <= laterAt && nextValue is { } later && value > later)
            throw new ConflictException("Leitura retroativa maior que a posterior. Corrija com justificativa, sem alterar manutenções encerradas.", "fleet.meter_out_of_order");
    }

    public static void ValidateWorkOrderTransition(string status, string? performed, string? cancellationReason)
    {
        if (status == "COMPLETED" && string.IsNullOrWhiteSpace(performed)) throw new DomainException("Informe os serviços realizados.", "fleet.completion_required");
        if (status is "CANCELLED" or "REOPENED" && string.IsNullOrWhiteSpace(cancellationReason))
            throw new DomainException("Informe o motivo do cancelamento ou da reabertura.", "fleet.reason_required");
    }

    public static void EnsureWorkOrderTransition(string from, string to)
    {
        var allowed = (from.ToUpperInvariant(), to.ToUpperInvariant()) switch
        {
            ("OPEN", "PLANNED") => true,
            ("OPEN", "PENDING_APPROVAL") => true,
            ("OPEN", "IN_PROGRESS") => true,
            ("OPEN", "CANCELLED") => true,
            ("PENDING_APPROVAL", "PLANNED") => true,
            ("PENDING_APPROVAL", "CANCELLED") => true,
            ("PLANNED", "IN_PROGRESS") => true,
            ("PLANNED", "WAITING_PART") => true,
            ("PLANNED", "CANCELLED") => true,
            ("IN_PROGRESS", "PAUSED") => true,
            ("IN_PROGRESS", "WAITING_PART") => true,
            ("IN_PROGRESS", "WAITING_VENDOR") => true,
            ("IN_PROGRESS", "INSPECTION") => true,
            ("IN_PROGRESS", "COMPLETED") => true,
            ("PAUSED", "IN_PROGRESS") => true,
            ("PAUSED", "CANCELLED") => true,
            ("WAITING_PART", "IN_PROGRESS") => true,
            ("WAITING_PART", "CANCELLED") => true,
            ("WAITING_VENDOR", "IN_PROGRESS") => true,
            ("WAITING_VENDOR", "CANCELLED") => true,
            ("INSPECTION", "COMPLETED") => true,
            ("INSPECTION", "IN_PROGRESS") => true,
            ("COMPLETED", "REOPENED") => true,
            ("REOPENED", "IN_PROGRESS") => true,
            _ => false
        };
        if (!allowed)
            throw new ConflictException($"Transição de ordem {from} → {to} não é permitida.", "fleet.transition_invalid");
    }

    public static decimal RefuelingTotal(decimal quantity, decimal unitPrice)
    {
        if (quantity <= 0) throw new DomainException("Quantidade deve ser positiva.", "fleet.quantity_positive");
        if (unitPrice < 0) throw new DomainException("Valor unitário não pode ser negativo.", "fleet.price_negative");
        return decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal Availability(int periodMinutes, int downtimeMinutes)
    {
        if (periodMinutes <= 0) return 0;
        return decimal.Round(Math.Max(0, periodMinutes - downtimeMinutes) * 100m / periodMinutes, 2);
    }

    public static string PlanDueState(DateTimeOffset? nextAt, decimal? nextMeter, decimal? currentMeter, bool hasValidReading)
    {
        if (nextMeter is not null && !hasValidReading) return "INSUFFICIENT_DATA";
        if (nextAt is { } due && due < DateTimeOffset.UtcNow) return "OVERDUE";
        if (nextMeter is { } meter && currentMeter is { } current && current >= meter) return "OVERDUE";
        if (nextAt is { } soon && soon <= DateTimeOffset.UtcNow.AddDays(15)) return "DUE_SOON";
        if (nextMeter is { } target && currentMeter is { } now && now >= target * 0.9m) return "DUE_SOON";
        return "ON_SCHEDULE";
    }

    public static void EnsureCsvSafe(ref string value)
    {
        if (value.Length == 0) return;
        if (value[0] is '=' or '+' or '-' or '@' or '\t')
            value = "'" + value;
    }
}
