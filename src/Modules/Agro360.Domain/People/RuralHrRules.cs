using Agro360.Domain.Tenancy;

namespace Agro360.Domain.People;

public readonly record struct RuralHrTariffCandidate(Guid Id, Guid? RoleId, string? ActivityType, string RateType, decimal RateValue, DateOnly ValidFrom);

public static class RuralHrRules
{
    public static decimal WorkedHours(DateTimeOffset start, DateTimeOffset end, int breakMinutes)
    { if (end <= start) throw new ArgumentException("A saída deve ser posterior à entrada."); if (breakMinutes < 0) throw new ArgumentException("A pausa não pode ser negativa."); var hours = (decimal)(end - start).TotalHours - breakMinutes / 60m; if (hours <= 0 || hours > 24) throw new ArgumentException("Jornada inválida."); return decimal.Round(hours, 2); }
    public static decimal LaborCost(decimal hours, decimal rate, string type, decimal quantity = 1) => type switch { "HOURLY" => hours * rate, "DAILY" => rate, "PIECEWORK" => quantity * rate, "FIXED" => rate, _ => throw new ArgumentException("Modalidade de custo inválida.") };
    public static void EnsureCapacity(int capacity, int passengers) { if (capacity <= 0 || passengers < 0 || passengers > capacity) throw new ArgumentException("A lotação excede a capacidade do transporte."); }
    public static bool IsExpired(DateOnly expiresOn, DateOnly today) => expiresOn < today;

    public static string InitialStatus(string kind) => (kind ?? "").Trim().ToUpperInvariant() switch
    {
        "PERSON" => "ACTIVE",
        "TIME_ENTRY" => "OPEN",
        "TEAM" => "ACTIVE",
        "TRAINING" => "PLANNED",
        "PPE" => "AVAILABLE",
        "INCIDENT" => "OPEN",
        "CORRECTIVE_ACTION" => "OPEN",
        "TRANSPORT" => "SCHEDULED",
        "INSPECTION" => "PLANNED",
        "RISK" => "OPEN",
        "ACCOMMODATION" => "AVAILABLE",
        "ALLOCATION" => "ACTIVE",
        "LABOR_COST" => "ACTIVE",
        _ => "ACTIVE"
    };

    /// <summary>
    /// Períodos [início, fim) conflitam quando se cruzam. Extremos encostados não conflitam.
    /// A mesma pessoa, a mesma equipe ou o integrante indicado em TEAM.person_id não podem ter alocações ACTIVE sobrepostas.
    /// Não há tabela de membros: a composição real é no máximo uma pessoa por equipe.
    /// </summary>
    public static bool PeriodsOverlap(DateTimeOffset start, DateTimeOffset end, DateTimeOffset otherStart, DateTimeOffset otherEnd)
        => start < otherEnd && end > otherStart;

    /// <summary>
    /// CPF com 11 dígitos ou CNPJ com 14, com dígitos verificadores.
    /// Tamanho intermediário não é documento. A fila de suspeita não declara o documento falso.
    /// </summary>
    public static string RequireWorkerDocument(string document)
    {
        try
        {
            return SaasGovernanceRules.NormalizeAndValidateDocument(document);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("Informe um CPF ou CNPJ válido. O cadastro não gera documento.");
        }
    }

    /// <summary>
    /// O início da jornada precisa cair em [início planejado, fim planejado).
    /// A saída posterior ao fim planejado é aceita e marcada como ultrapassagem; não reescreve a alocação.
    /// </summary>
    public static string PlannedCoverage(DateTimeOffset started, DateTimeOffset? ended, DateTimeOffset planStart, DateTimeOffset planEnd)
    {
        if (started < planStart || started >= planEnd) return "START_OUTSIDE";
        if (ended is not null && ended.Value > planEnd) return "OVERRUN";
        return "INSIDE";
    }

    /// <summary>
    /// Precedência da tarifa na data da jornada: papel+atividade (3), só papel (2), só atividade (1), genérica (0).
    /// Empate de especificidade com o mesmo valor usa a vigência mais recente e, depois, o menor id.
    /// Empate com valores diferentes é ambíguo e não escolhe uma tarifa.
    /// </summary>
    public static bool TrySelectTariff(IReadOnlyList<RuralHrTariffCandidate> candidates, Guid? roleId, string? activity, out RuralHrTariffCandidate selected)
    {
        selected = default;
        if (candidates.Count == 0) return false;
        var ranked = new List<(RuralHrTariffCandidate Tariff, int Score)>();
        foreach (var candidate in candidates)
        {
            var score = TariffScore(candidate, roleId, activity);
            if (score >= 0) ranked.Add((candidate, score));
        }
        if (ranked.Count == 0) return false;
        var best = ranked.Max(x => x.Score);
        var top = ranked.Where(x => x.Score == best).Select(x => x.Tariff).ToArray();
        if (top.Select(x => (Normalize(x.RateType), x.RateValue)).Distinct().Count() > 1)
            throw new InvalidOperationException("Há mais de uma tarifa com a mesma precedência e valores diferentes.");
        selected = top.OrderByDescending(x => x.ValidFrom).ThenBy(x => x.Id).First();
        return true;
    }

    /// <summary>
    /// Quatro casas, AwayFromZero, alinhado ao lançamento de custo da safra.
    /// PIECEWORK sem quantidade informada devolve nulo: ausência não vira zero.
    /// </summary>
    public static decimal? QuoteLabor(string rateType, decimal rate, decimal hours, decimal? pieceQuantity)
    {
        var type = Normalize(rateType);
        if (rate < 0) throw new ArgumentException("A tarifa não pode ser negativa.");
        decimal quantity = 1m;
        if (type == "PIECEWORK")
        {
            if (pieceQuantity is null) return null;
            if (pieceQuantity < 0) throw new ArgumentException("A quantidade de produção não pode ser negativa.");
            quantity = pieceQuantity.Value;
        }
        var raw = LaborCost(hours, rate, type, quantity);
        return decimal.Round(raw, 4, MidpointRounding.AwayFromZero);
    }

    public static string TariffUnit(string rateType) => Normalize(rateType) switch
    {
        "HOURLY" => "HOUR",
        "DAILY" => "DAY",
        "PIECEWORK" => "UNIT",
        "FIXED" => "CONTRACT",
        _ => throw new ArgumentException("Modalidade de custo inválida.")
    };

    private static int TariffScore(RuralHrTariffCandidate candidate, Guid? roleId, string? activity)
    {
        var roleSpecific = candidate.RoleId.HasValue;
        var activitySpecific = !string.IsNullOrWhiteSpace(candidate.ActivityType);
        if (roleSpecific && candidate.RoleId != roleId) return -1;
        if (activitySpecific && !string.Equals(Normalize(candidate.ActivityType), Normalize(activity), StringComparison.Ordinal)) return -1;
        return (roleSpecific ? 2 : 0) + (activitySpecific ? 1 : 0);
    }

    private static string Normalize(string? value) => (value ?? "").Trim().ToUpperInvariant();

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
                ("ACTIVE", "AVAILABLE") => true,
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
                ("ACTIVE", "OPEN") => true,
                ("ACTIVE", "INVESTIGATING") => true,
                ("ACTIVE", "CLOSED") => true,
                ("ACTIVE", "COMPLETED") => true,
                ("INVESTIGATING", "CLOSED") => true,
                ("INVESTIGATING", "COMPLETED") => true,
                _ => false
            },
            "CORRECTIVE_ACTION" => (cur, tgt) switch
            {
                ("OPEN", "IN_PROGRESS") => true,
                ("OPEN", "COMPLETED") => true,
                ("OPEN", "CANCELLED") => true,
                ("ACTIVE", "OPEN") => true,
                ("ACTIVE", "IN_PROGRESS") => true,
                ("ACTIVE", "COMPLETED") => true,
                ("ACTIVE", "CANCELLED") => true,
                ("IN_PROGRESS", "COMPLETED") => true,
                ("IN_PROGRESS", "CANCELLED") => true,
                _ => false
            },
            "TRANSPORT" => (cur, tgt) switch
            {
                ("SCHEDULED", "IN_TRANSIT") => true,
                ("SCHEDULED", "COMPLETED") => true,
                ("SCHEDULED", "CANCELLED") => true,
                ("ACTIVE", "SCHEDULED") => true,
                ("ACTIVE", "IN_TRANSIT") => true,
                ("ACTIVE", "COMPLETED") => true,
                ("ACTIVE", "CANCELLED") => true,
                ("IN_TRANSIT", "COMPLETED") => true,
                _ => false
            },
            "RISK" => (cur, tgt) switch
            {
                ("OPEN", "MITIGATED") => true,
                ("OPEN", "CLOSED") => true,
                ("ACTIVE", "MITIGATED") => true,
                ("ACTIVE", "CLOSED") => true,
                _ => false
            },
            "INSPECTION" => (cur, tgt) switch
            {
                ("PLANNED", "COMPLETED") => true,
                ("PLANNED", "CANCELLED") => true,
                ("ACTIVE", "COMPLETED") => true,
                ("ACTIVE", "CANCELLED") => true,
                _ => false
            },
            "ACCOMMODATION" => (cur, tgt) switch
            {
                ("AVAILABLE", "OCCUPIED") => true,
                ("OCCUPIED", "AVAILABLE") => true,
                ("AVAILABLE", "MAINTENANCE") => true,
                ("MAINTENANCE", "AVAILABLE") => true,
                ("ACTIVE", "OCCUPIED") => true,
                ("ACTIVE", "AVAILABLE") => true,
                _ => false
            },
            "ALLOCATION" => (cur, tgt) switch
            {
                ("ACTIVE", "CANCELLED") => true,
                ("ACTIVE", "COMPLETED") => true,
                ("ACTIVE", "INACTIVE") => true,
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

