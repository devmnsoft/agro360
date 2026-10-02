namespace Agro360.Domain.People;

public static class RuralHrRules
{
    public static decimal WorkedHours(DateTimeOffset start, DateTimeOffset end, int breakMinutes)
    { if (end <= start) throw new ArgumentException("A saída deve ser posterior à entrada."); if (breakMinutes < 0) throw new ArgumentException("A pausa não pode ser negativa."); var hours = (decimal)(end - start).TotalHours - breakMinutes / 60m; if (hours <= 0 || hours > 24) throw new ArgumentException("Jornada inválida."); return decimal.Round(hours, 2); }
    public static decimal LaborCost(decimal hours, decimal rate, string type, decimal quantity = 1) => type switch { "HOURLY" => hours * rate, "DAILY" => rate, "PIECEWORK" => quantity * rate, "FIXED" => rate, _ => throw new ArgumentException("Modalidade de custo inválida.") };
    public static void EnsureCapacity(int capacity, int passengers) { if (capacity <= 0 || passengers < 0 || passengers > capacity) throw new ArgumentException("A lotação excede a capacidade do transporte."); }
    public static bool IsExpired(DateOnly expiresOn, DateOnly today) => expiresOn < today;

    public static void ValidateStatusTransition(string kind, string currentStatus, string targetStatus)
    {
        var cur = (currentStatus ?? "").Trim().ToUpperInvariant();
        var tgt = (targetStatus ?? "").Trim().ToUpperInvariant();
        if (string.Equals(cur, tgt, StringComparison.Ordinal)) return;

        var k = (kind ?? "").Trim().ToUpperInvariant();
        bool allowed = k switch
        {
            "PERSON" => (cur, tgt) switch
            {
                ("ACTIVE", "INACTIVE") => true,
                ("INACTIVE", "ACTIVE") => true,
                _ => false
            },
            "TIME_ENTRY" => (cur, tgt) switch
            {
                ("OPEN", "CLOSED") => true,
                _ => false
            },
            "TEAM" => (cur, tgt) switch
            {
                ("ACTIVE", "IN_FIELD") => true,
                ("ACTIVE", "INACTIVE") => true,
                ("IN_FIELD", "ACTIVE") => true,
                ("IN_FIELD", "INACTIVE") => true,
                ("INACTIVE", "ACTIVE") => true,
                _ => false
            },
            "TRAINING" => (cur, tgt) switch
            {
                ("PLANNED", "ACTIVE") => true,
                ("PLANNED", "COMPLETED") => true,
                ("PLANNED", "CANCELLED") => true,
                ("ACTIVE", "COMPLETED") => true,
                ("ACTIVE", "CANCELLED") => true,
                _ => false
            },
            "PPE" => (cur, tgt) switch
            {
                ("AVAILABLE", "DELIVERED") => true,
                ("AVAILABLE", "INACTIVE") => true,
                ("ACTIVE", "DELIVERED") => true,
                ("ACTIVE", "INACTIVE") => true,
                ("DELIVERED", "RETURNED") => true,
                ("DELIVERED", "DISCARDED") => true,
                ("RETURNED", "AVAILABLE") => true,
                ("RETURNED", "ACTIVE") => true,
                ("RETURNED", "DISCARDED") => true,
                _ => false
            },
            "INCIDENT" => (cur, tgt) switch
            {
                ("OPEN", "INVESTIGATING") => true,
                ("OPEN", "CLOSED") => true,
                ("OPEN", "COMPLETED") => true,
                ("INVESTIGATING", "CLOSED") => true,
                ("INVESTIGATING", "COMPLETED") => true,
                _ => false
            },
            "CORRECTIVE_ACTION" => (cur, tgt) switch
            {
                ("OPEN", "IN_PROGRESS") => true,
                ("OPEN", "COMPLETED") => true,
                ("OPEN", "CANCELLED") => true,
                ("IN_PROGRESS", "COMPLETED") => true,
                ("IN_PROGRESS", "CANCELLED") => true,
                _ => false
            },
            "TRANSPORT" => (cur, tgt) switch
            {
                ("SCHEDULED", "IN_TRANSIT") => true,
                ("SCHEDULED", "COMPLETED") => true,
                ("SCHEDULED", "CANCELLED") => true,
                ("ACTIVE", "IN_TRANSIT") => true,
                ("ACTIVE", "COMPLETED") => true,
                ("ACTIVE", "CANCELLED") => true,
                ("IN_TRANSIT", "COMPLETED") => true,
                _ => false
            },
            _ => (cur, tgt) switch
            {
                ("ACTIVE", "INACTIVE") => true,
                ("ACTIVE", "COMPLETED") => true,
                ("INACTIVE", "ACTIVE") => true,
                ("OPEN", "COMPLETED") => true,
                ("OPEN", "CLOSED") => true,
                _ => false
            }
        };

        if (!allowed)
            throw new InvalidOperationException($"Transição de status inválida para {k}: de '{cur}' para '{tgt}'.");
    }
}

