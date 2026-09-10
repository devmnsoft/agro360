using Agro360.SharedKernel;

namespace Agro360.Domain.Livestock;

public static class LivestockRules
{
    public static readonly string[] ControlModes = ["INDIVIDUAL", "QUANTITY"];
    public static readonly string[] MovementKinds = ["ENTRY", "TRANSFER", "LOT_CHANGE", "LOCATION_CHANGE", "EXIT", "ADJUST", "REVERSAL"];
    public static readonly string[] EntryOrigins = ["PURCHASE", "BIRTH", "TRANSFER_IN", "INVENTORY", "OTHER"];
    public static readonly string[] ExitReasons = ["SALE", "DEATH", "DISCARD", "TRANSFER_OUT"];
    public static readonly string[] HandlingOrderStatuses = ["DRAFT", "SCHEDULED", "RELEASED", "IN_PROGRESS", "PAUSED", "COMPLETED", "CANCELLED"];
    public static readonly string[] HandlingOutcomes = ["PLANNED", "ATTENDED", "NOT_ATTENDED", "BLOCKED", "FAILED"];
    public static readonly string[] RestrictionPurposes = ["MILK", "SLAUGHTER", "SALE", "OPERATIONAL"];
    public static readonly string[] CostNatures = ["REALIZED", "COMMITMENT", "ESTIMATE"];
    public static readonly short[] OnFarmStatuses = [1, 2, 6];
    public static readonly short[] TerminalStatuses = [3, 4, 5];

    public static decimal NonNegative(decimal value, string field) { if (value < 0) throw new DomainException($"{field} não pode ser negativo.", "livestock.negative_value"); return value; }
    public static decimal Positive(decimal value, string field) { if (value <= 0) throw new DomainException($"{field} deve ser positivo.", "livestock.positive_required"); return value; }
    public static decimal AverageDailyGain(decimal initialWeight, DateOnly initialDate, decimal finalWeight, DateOnly finalDate) { NonNegative(initialWeight, "Peso inicial"); NonNegative(finalWeight, "Peso final"); var days = finalDate.DayNumber - initialDate.DayNumber; if (days <= 0) throw new DomainException("A pesagem final deve ser posterior à inicial.", "livestock.weight_period_invalid"); return decimal.Round((finalWeight - initialWeight) / days, 4); }
    public static DateOnly ExpectedBirth(DateOnly eventDate, string species) => eventDate.AddDays(species.Trim().ToUpperInvariant() switch { "BOVINE" or "BOVINO" => 283, "BUFFALO" or "BUBALINO" => 310, "SHEEP" or "OVINO" => 150, "GOAT" or "CAPRINO" => 150, "SWINE" or "SUINO" or "SUÍNO" => 114, _ => 280 });
    public static void RequireFemale(string sex) { if (!new[] { "F", "FEMALE", "FEMEA", "FÊMEA" }.Contains(sex.Trim().ToUpperInvariant())) throw new DomainException("Somente fêmeas aptas podem iniciar gestação.", "livestock.female_required"); }
    public static decimal DietCost(IEnumerable<(decimal Quantity, decimal UnitCost)> items) { var list = items.ToArray(); if (list.Length == 0) throw new DomainException("A dieta deve conter itens.", "livestock.diet_empty"); foreach (var i in list) { Positive(i.Quantity, "Quantidade"); NonNegative(i.UnitCost, "Custo"); } return list.Sum(i => i.Quantity * i.UnitCost); }

    public static void EnsureOnFarm(short status, string operation)
    {
        if (TerminalStatuses.Contains(status))
            throw new ConflictException($"Não é possível {operation} um animal já vendido, morto ou descartado.", "livestock.animal_not_active");
    }

    public static void EnsureEventNotBeforeBirth(DateOnly occurredOn, DateOnly birthDate, bool estimated)
    {
        if (!estimated && occurredOn < birthDate)
            throw new DomainException("O evento não pode ser anterior à data de nascimento conhecida.", "livestock.event_before_birth");
    }

    public static void EnsureNoSelfParent(Guid animalId, Guid? motherId, Guid? fatherId)
    {
        if (motherId == animalId || fatherId == animalId)
            throw new DomainException("A filiação não pode apontar para o próprio animal.", "livestock.parentage_cycle");
    }

    public static void EnsureDistinctParents(Guid? motherId, Guid? fatherId)
    {
        if (motherId.HasValue && fatherId.HasValue && motherId == fatherId)
            throw new DomainException("Mãe e pai não podem ser o mesmo animal.", "livestock.parentage_invalid");
    }

    public static void EnsureControlMode(string mode)
    {
        if (!ControlModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
            throw new DomainException("Informe se o grupo é controlado por indivíduos ou por quantidade.", "livestock.control_mode_invalid");
    }

    public static void PreventDoubleCount(string herdMode, bool assigningAnimal)
    {
        if (assigningAnimal && string.Equals(herdMode, "QUANTITY", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Grupo controlado por quantidade não recebe animais individualizados. Concilie antes de identificar cabeças.", "livestock.double_count");
    }

    public static void EnsureHandlingTransition(string from, string to)
    {
        var allowed = (from.ToUpperInvariant(), to.ToUpperInvariant()) switch
        {
            ("DRAFT", "SCHEDULED") => true,
            ("DRAFT", "CANCELLED") => true,
            ("SCHEDULED", "RELEASED") => true,
            ("SCHEDULED", "CANCELLED") => true,
            ("RELEASED", "IN_PROGRESS") => true,
            ("RELEASED", "CANCELLED") => true,
            ("IN_PROGRESS", "PAUSED") => true,
            ("IN_PROGRESS", "COMPLETED") => true,
            ("IN_PROGRESS", "CANCELLED") => true,
            ("PAUSED", "IN_PROGRESS") => true,
            ("PAUSED", "CANCELLED") => true,
            _ => false
        };
        if (!allowed)
            throw new ConflictException($"Transição de manejo {from} → {to} não é permitida.", "livestock.handling_transition_invalid");
    }

    public static void EnsureCompletionCounts(int planned, int attended, int notAttended, int blocked, int stillPlanned)
    {
        if (planned <= 0)
            throw new DomainException("A ordem precisa de população planejada.", "livestock.handling_empty");
        if (stillPlanned > 0)
            throw new DomainException("Conclusão exige apontamento válido de todos os animais planejados.", "livestock.handling_pending_items");
        if (attended + notAttended + blocked != planned)
            throw new DomainException("A soma de atendidos, não atendidos e impedidos deve coincidir com o planejado.", "livestock.handling_outcome_mismatch");
    }

    public static void RejectSilentFullAttendance(int planned, int attended)
    {
        if (planned > 0 && attended < planned)
            throw new ConflictException("Manejo parcialmente executado não pode ser marcado como atendimento integral.", "livestock.handling_partial");
    }

    public static decimal ConvertWeight(decimal value, string fromUnit, string toUnit, decimal? factor)
    {
        Positive(value, "Peso");
        if (string.Equals(fromUnit, toUnit, StringComparison.OrdinalIgnoreCase))
            return decimal.Round(value, 3);
        if (factor is null or <= 0)
            throw new DomainException("Conversão de unidade exige fator válido e explícito.", "livestock.conversion_required");
        return decimal.Round(value * factor.Value, 3);
    }

    public static void EnsurePositiveQuantity(int quantity, string field)
    {
        if (quantity <= 0)
            throw new DomainException($"{field} deve ser positivo.", "livestock.quantity_invalid");
    }

    public static void EnsureCsvSafe(ref string value)
    {
        if (value.Length == 0) return;
        var first = value[0];
        if (first is '=' or '+' or '-' or '@' or '\t')
            value = "'" + value;
    }
}
