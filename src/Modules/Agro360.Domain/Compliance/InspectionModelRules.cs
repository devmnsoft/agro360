namespace Agro360.Domain.Compliance;

/// <summary>
/// Pure business rules for quality inspection models, versions, selection and run evaluation.
/// Weighted scoring excludes N/A from the base; a critical fail always overrides any score.
/// </summary>
public static class InspectionModelRules
{
    public static readonly HashSet<string> ValidProcessCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PURCHASE_RECEIPT", "HARVEST_RECEIPT", "PRODUCTION", "STORAGE", "SHIPMENT", "RETURN"
    };

    public static readonly HashSet<string> ValidCriterionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PASS_FAIL", "SINGLE_CHOICE", "MULTI_CHOICE", "TEXT", "NUMBER", "DATE", "DOCUMENT_EVIDENCE"
    };

    private static readonly Dictionary<string, string[]> AllowedVersionTransitions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["DRAFT"] = ["IN_REVIEW"],
            ["IN_REVIEW"] = ["PUBLISHED", "DRAFT"],
            ["PUBLISHED"] = ["SUPERSEDED", "INACTIVE"],
            ["SUPERSEDED"] = [],
            ["INACTIVE"] = []
        };

    public static void EnsureValidProcessCode(string processCode)
    {
        if (string.IsNullOrWhiteSpace(processCode) || !ValidProcessCodes.Contains(processCode))
            throw new ArgumentException("Código de processo de inspeção inválido.", nameof(processCode));
    }

    public static void EnsureValidCriterionType(string criterionType)
    {
        if (string.IsNullOrWhiteSpace(criterionType) || !ValidCriterionTypes.Contains(criterionType))
            throw new ArgumentException("Tipo de critério de inspeção inválido.", nameof(criterionType));
    }

    public static IReadOnlyList<string> AvailableVersionTransitions(string currentStatus)
    {
        if (string.IsNullOrWhiteSpace(currentStatus) || !AllowedVersionTransitions.TryGetValue(currentStatus, out var allowed))
            throw new ArgumentException("Estado da versão do modelo é desconhecido.", nameof(currentStatus));
        return Array.AsReadOnly((string[])allowed.Clone());
    }

    public static void EnsureVersionTransition(string currentStatus, string targetStatus)
    {
        if (string.IsNullOrWhiteSpace(currentStatus) || !AllowedVersionTransitions.ContainsKey(currentStatus))
            throw new ArgumentException("Estado atual da versão do modelo é desconhecido.", nameof(currentStatus));
        if (string.IsNullOrWhiteSpace(targetStatus))
            throw new ArgumentException("Informe o estado de destino da versão.", nameof(targetStatus));
        if (!AllowedVersionTransitions.TryGetValue(currentStatus, out var allowed) ||
            !allowed.Contains(targetStatus, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Transição de versão de {currentStatus} para {targetStatus} não permitida.");
    }

    /// <summary>Only DRAFT versions are editable; published content is immutable.</summary>
    public static void EnsureVersionEditable(string versionStatus)
    {
        if (string.IsNullOrWhiteSpace(versionStatus))
            throw new ArgumentException("Estado da versão é obrigatório.", nameof(versionStatus));
        if (!versionStatus.Equals("DRAFT", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Somente versões em rascunho (DRAFT) podem ser editadas; conteúdo publicado é imutável.");
    }

    public static void EnsureCanPublish(string versionStatus, bool hasSections, bool hasCriteria, DateOnly? validFrom)
    {
        if (!versionStatus.Equals("IN_REVIEW", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Somente versões em revisão (IN_REVIEW) podem ser publicadas.");
        if (!hasSections)
            throw new InvalidOperationException("A publicação exige ao menos uma seção.");
        if (!hasCriteria)
            throw new InvalidOperationException("A publicação exige ao menos um critério.");
        if (!validFrom.HasValue)
            throw new ArgumentException("A publicação exige data de início de vigência (valid_from).", nameof(validFrom));
    }

    public static void EnsureCanStartInspection(
        string modelStatus,
        string versionStatus,
        DateOnly validFrom,
        DateOnly? validUntil,
        DateOnly referenceDate)
    {
        if (modelStatus.Equals("INACTIVE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Modelo de inspeção inativo não pode iniciar execução.");
        if (!versionStatus.Equals("PUBLISHED", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Somente versões publicadas podem iniciar inspeção.");
        if (referenceDate < validFrom)
            throw new InvalidOperationException("A data de referência está antes do início de vigência do modelo.");
        if (validUntil.HasValue && referenceDate > validUntil.Value)
            throw new InvalidOperationException("A data de referência está fora da vigência do modelo.");
    }

    /// <summary>
    /// Selects a single winner by highest specificity, then lowest precedence.
    /// Ties on both dimensions are ambiguous and never resolved by arbitrary order.
    /// </summary>
    public static ModelSelectionResult SelectApplicableModel(IEnumerable<ModelSelectionCandidate> candidates)
    {
        var list = candidates?.ToArray() ?? [];
        if (list.Length == 0)
            return new ModelSelectionResult(false, null, Array.Empty<Guid>(), "Nenhum modelo candidato aplicável.");

        var bestSpecificity = list.Max(c => c.SpecificityScore);
        var topSpecificity = list.Where(c => c.SpecificityScore == bestSpecificity).ToArray();
        var bestPrecedence = topSpecificity.Min(c => c.Precedence);
        var winners = topSpecificity.Where(c => c.Precedence == bestPrecedence).ToArray();

        if (winners.Length > 1)
        {
            var tied = winners.Select(w => w.ModelId).ToArray();
            return new ModelSelectionResult(
                true,
                null,
                tied,
                $"Ambiguidade: {tied.Length} modelos empatam em especificidade {bestSpecificity} e precedência {bestPrecedence}.");
        }

        var winner = winners[0];
        return new ModelSelectionResult(
            false,
            winner.ModelId,
            Array.Empty<Guid>(),
            $"Selecionado por especificidade {winner.SpecificityScore} e precedência {winner.Precedence}.");
    }

    public static void EnsureAnswerValid(InspectionCriterionDefinition criterion, InspectionAnswerInput answer)
    {
        ArgumentNullException.ThrowIfNull(criterion);
        ArgumentNullException.ThrowIfNull(answer);
        EnsureValidCriterionType(criterion.CriterionType);

        if (answer.IsNotApplicable)
        {
            if (!criterion.AllowNotApplicable)
                throw new InvalidOperationException($"O critério '{criterion.StableKey}' não permite N/A.");
            if (criterion.RequireNaJustification && string.IsNullOrWhiteSpace(answer.NotApplicableJustification))
                throw new InvalidOperationException($"O critério '{criterion.StableKey}' exige justificativa para N/A.");
            return;
        }

        var type = criterion.CriterionType.ToUpperInvariant();
        switch (type)
        {
            case "PASS_FAIL":
                if (!answer.PassFailValue.HasValue && criterion.Required)
                    throw new InvalidOperationException($"O critério obrigatório '{criterion.StableKey}' não foi respondido.");
                break;
            case "SINGLE_CHOICE":
                if (criterion.Required && (answer.ChoiceValues is null || answer.ChoiceValues.Count == 0 || string.IsNullOrWhiteSpace(answer.ChoiceValues[0])))
                    throw new InvalidOperationException($"O critério obrigatório '{criterion.StableKey}' não foi respondido.");
                if (answer.ChoiceValues is { Count: > 0 } && criterion.Options is { Count: > 0 } &&
                    !criterion.Options.Contains(answer.ChoiceValues[0], StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException($"Opção inválida para o critério '{criterion.StableKey}'.", nameof(answer));
                break;
            case "MULTI_CHOICE":
                if (criterion.Required && (answer.ChoiceValues is null || answer.ChoiceValues.Count == 0))
                    throw new InvalidOperationException($"O critério obrigatório '{criterion.StableKey}' não foi respondido.");
                if (answer.ChoiceValues is { Count: > 0 } && criterion.Options is { Count: > 0 })
                {
                    var invalid = answer.ChoiceValues.FirstOrDefault(v => !criterion.Options.Contains(v, StringComparer.OrdinalIgnoreCase));
                    if (invalid is not null)
                        throw new ArgumentException($"Opção '{invalid}' inválida para o critério '{criterion.StableKey}'.", nameof(answer));
                }
                break;
            case "TEXT":
                if (criterion.Required && string.IsNullOrWhiteSpace(answer.TextValue))
                    throw new InvalidOperationException($"O critério obrigatório '{criterion.StableKey}' não foi respondido.");
                break;
            case "NUMBER":
                // Absent number is never treated as zero. Unit mismatch is not compared here;
                // EvaluateCriterionConformity returns Inconclusive when units differ.
                if (criterion.Required && !answer.NumberValue.HasValue)
                    throw new InvalidOperationException($"O critério obrigatório '{criterion.StableKey}' exige valor numérico; ausência não representa zero.");
                break;
            case "DATE":
                if (criterion.Required && !answer.DateValue.HasValue)
                    throw new InvalidOperationException($"O critério obrigatório '{criterion.StableKey}' não foi respondido.");
                break;
            case "DOCUMENT_EVIDENCE":
                if (criterion.Required && !answer.HasDocumentEvidence && answer.DocumentEvidenceId is null)
                    throw new InvalidOperationException($"O critério obrigatório '{criterion.StableKey}' exige evidência documental.");
                break;
        }
    }

    /// <summary>Evaluates a single criterion against structured approval_condition semantics (no script engine).</summary>
    public static CriterionConformityResult EvaluateCriterionConformity(
        InspectionCriterionDefinition criterion,
        InspectionAnswerInput answer)
    {
        ArgumentNullException.ThrowIfNull(criterion);
        ArgumentNullException.ThrowIfNull(answer);
        EnsureValidCriterionType(criterion.CriterionType);

        if (answer.IsNotApplicable)
        {
            if (!criterion.AllowNotApplicable)
                return new CriterionConformityResult(CriterionOutcome.NonConforming, "N/A não permitido.");
            if (criterion.RequireNaJustification && string.IsNullOrWhiteSpace(answer.NotApplicableJustification))
                return new CriterionConformityResult(CriterionOutcome.Inconclusive, "N/A sem justificativa.");
            return new CriterionConformityResult(CriterionOutcome.NotApplicable, "Critério marcado como N/A.");
        }

        var type = criterion.CriterionType.ToUpperInvariant();
        var condition = criterion.ApprovalCondition;

        return type switch
        {
            "PASS_FAIL" => EvaluatePassFail(criterion.StableKey, answer.PassFailValue, condition),
            "SINGLE_CHOICE" => EvaluateSingleChoice(criterion.StableKey, answer.ChoiceValues, condition),
            "MULTI_CHOICE" => EvaluateMultiChoice(criterion.StableKey, answer.ChoiceValues, condition),
            "TEXT" => EvaluateText(criterion.StableKey, answer.TextValue, condition),
            "NUMBER" => EvaluateNumber(criterion.StableKey, answer.NumberValue, answer.NumberUnit, criterion.Unit, condition),
            "DATE" => EvaluateDate(criterion.StableKey, answer.DateValue, condition),
            "DOCUMENT_EVIDENCE" => EvaluateDocument(criterion.StableKey, answer.HasDocumentEvidence || answer.DocumentEvidenceId.HasValue, condition),
            _ => new CriterionConformityResult(CriterionOutcome.Inconclusive, "Tipo de critério sem avaliação.")
        };
    }

    public static OverallInspectionResult ComputeOverallResult(IEnumerable<InspectionCriterionAnswerState> answers)
    {
        var list = answers?.ToArray() ?? throw new ArgumentNullException(nameof(answers));

        var unansweredRequired = list.Where(a => a.Required && a.Outcome == CriterionOutcome.Unanswered).ToArray();
        if (unansweredRequired.Length > 0)
            throw new InvalidOperationException(
                $"Há critérios obrigatórios sem resposta: {string.Join(", ", unansweredRequired.Select(a => a.StableKey))}.");

        var criticalFails = list.Where(a => a.Critical && a.Outcome == CriterionOutcome.NonConforming).ToArray();
        if (criticalFails.Length > 0)
        {
            // Critical fail cannot be compensated by average / weighted score.
            return new OverallInspectionResult(
                "NON_CONFORMING",
                criticalFails.Select(a => a.StableKey).ToArray(),
                ComputeWeightedScore(list),
                "Falha crítica impede conformidade; pontuação ponderada não aprova o resultado.");
        }

        var nonCriticalFails = list.Where(a => !a.Critical && a.Outcome == CriterionOutcome.NonConforming).ToArray();
        if (nonCriticalFails.Length > 0)
        {
            return new OverallInspectionResult(
                "NON_CONFORMING",
                nonCriticalFails.Select(a => a.StableKey).ToArray(),
                ComputeWeightedScore(list),
                "Há critérios não conformes.");
        }

        var reviewNeeded = list.Where(a =>
            a.RequireReview &&
            (a.Outcome is CriterionOutcome.Inconclusive or CriterionOutcome.Unanswered)).ToArray();
        if (reviewNeeded.Length > 0)
        {
            return new OverallInspectionResult(
                "INCONCLUSIVE",
                reviewNeeded.Select(a => a.StableKey).ToArray(),
                ComputeWeightedScore(list),
                "Há itens inconclusivos ou opcionais sem resposta que exigem revisão.");
        }

        return new OverallInspectionResult(
            "CONFORMING",
            Array.Empty<string>(),
            ComputeWeightedScore(list),
            "Todos os critérios aplicáveis estão conformes. N/A excluídos da base ponderada.");
    }

    public static void EnsureReinspection(bool parentCompleted, string? reason)
    {
        if (!parentCompleted)
            throw new InvalidOperationException("A reinspeção exige que a inspeção pai esteja concluída.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reinspeção exige motivo.", nameof(reason));
    }

    public static void EnsureRowVersion(long expected, long actual)
    {
        if (expected != actual)
            throw new InvalidOperationException("O registro foi alterado por outro usuário. Recarregue e tente novamente.");
    }

    /// <summary>
    /// N/A answers are excluded from the weighted base. Returns null when no weights are present
    /// among evaluated (non-N/A) criteria. Critical fail still overrides any score at overall result.
    /// </summary>
    public static decimal? ComputeWeightedScore(IEnumerable<InspectionCriterionAnswerState> answers)
    {
        var scored = answers
            .Where(a => a.Outcome != CriterionOutcome.NotApplicable && a.Weight.HasValue && a.Weight.Value > 0)
            .ToArray();
        if (scored.Length == 0) return null;

        var baseWeight = scored.Sum(a => a.Weight!.Value);
        if (baseWeight <= 0) return null;

        var conformingWeight = scored
            .Where(a => a.Outcome == CriterionOutcome.Conforming)
            .Sum(a => a.Weight!.Value);
        return decimal.Round(conformingWeight * 100m / baseWeight, 2, MidpointRounding.AwayFromZero);
    }

    private static CriterionConformityResult EvaluatePassFail(string key, bool? value, ApprovalCondition? condition)
    {
        if (!value.HasValue)
            return new CriterionConformityResult(CriterionOutcome.Unanswered, $"Critério '{key}' sem resposta.");
        if (condition?.ExpectedPass is bool expected)
            return value.Value == expected
                ? new CriterionConformityResult(CriterionOutcome.Conforming, null)
                : new CriterionConformityResult(CriterionOutcome.NonConforming, "Resultado PASS/FAIL fora da condição.");
        // Default: PASS (true) is conforming.
        return value.Value
            ? new CriterionConformityResult(CriterionOutcome.Conforming, null)
            : new CriterionConformityResult(CriterionOutcome.NonConforming, "Resultado FAIL.");
    }

    private static CriterionConformityResult EvaluateSingleChoice(string key, IReadOnlyList<string>? choices, ApprovalCondition? condition)
    {
        var selected = choices is { Count: > 0 } ? choices[0] : null;
        if (string.IsNullOrWhiteSpace(selected))
            return new CriterionConformityResult(CriterionOutcome.Unanswered, $"Critério '{key}' sem resposta.");
        if (condition?.ExpectedChoices is { Count: > 0 } approved)
            return approved.Contains(selected, StringComparer.OrdinalIgnoreCase)
                ? new CriterionConformityResult(CriterionOutcome.Conforming, null)
                : new CriterionConformityResult(CriterionOutcome.NonConforming, "Opção fora das aprovadas.");
        if (!string.IsNullOrWhiteSpace(condition?.ExpectedText))
            return selected.Equals(condition.ExpectedText, StringComparison.OrdinalIgnoreCase)
                ? new CriterionConformityResult(CriterionOutcome.Conforming, null)
                : new CriterionConformityResult(CriterionOutcome.NonConforming, "Opção diferente da esperada.");
        return new CriterionConformityResult(CriterionOutcome.Inconclusive, "Condição de aprovação ausente para escolha única.");
    }

    private static CriterionConformityResult EvaluateMultiChoice(string key, IReadOnlyList<string>? choices, ApprovalCondition? condition)
    {
        if (choices is null || choices.Count == 0)
            return new CriterionConformityResult(CriterionOutcome.Unanswered, $"Critério '{key}' sem resposta.");
        if (condition?.ExpectedChoices is not { Count: > 0 } approved)
            return new CriterionConformityResult(CriterionOutcome.Inconclusive, "Condição de aprovação ausente para múltipla escolha.");

        var op = condition.Operator?.ToUpperInvariant() ?? "ALL_IN";
        if (op is "ALL_IN" or "EQ")
        {
            var allMatch = choices.All(c => approved.Contains(c, StringComparer.OrdinalIgnoreCase)) &&
                           choices.Count == approved.Count &&
                           approved.All(a => choices.Contains(a, StringComparer.OrdinalIgnoreCase));
            // ALL_IN: every selected must be approved; if operator EQ, sets must match.
            if (op == "EQ")
                return allMatch
                    ? new CriterionConformityResult(CriterionOutcome.Conforming, null)
                    : new CriterionConformityResult(CriterionOutcome.NonConforming, "Conjunto de opções diferente do esperado.");
            var subsetOk = choices.All(c => approved.Contains(c, StringComparer.OrdinalIgnoreCase));
            return subsetOk
                ? new CriterionConformityResult(CriterionOutcome.Conforming, null)
                : new CriterionConformityResult(CriterionOutcome.NonConforming, "Há opções fora das aprovadas.");
        }

        if (op == "IN")
        {
            var any = choices.Any(c => approved.Contains(c, StringComparer.OrdinalIgnoreCase));
            return any
                ? new CriterionConformityResult(CriterionOutcome.Conforming, null)
                : new CriterionConformityResult(CriterionOutcome.NonConforming, "Nenhuma opção aprovada selecionada.");
        }

        return new CriterionConformityResult(CriterionOutcome.Inconclusive, $"Operador '{op}' não suportado para MULTI_CHOICE.");
    }

    private static CriterionConformityResult EvaluateText(string key, string? text, ApprovalCondition? condition)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new CriterionConformityResult(CriterionOutcome.Unanswered, $"Critério '{key}' sem resposta.");
        if (condition is null || string.IsNullOrWhiteSpace(condition.Operator))
            return new CriterionConformityResult(CriterionOutcome.Conforming, "Texto informado.");
        var op = condition.Operator.ToUpperInvariant();
        if (op is "PRESENT" or "ANY")
            return new CriterionConformityResult(CriterionOutcome.Conforming, null);
        if (op == "EQ" && !string.IsNullOrWhiteSpace(condition.ExpectedText))
            return text.Equals(condition.ExpectedText, StringComparison.OrdinalIgnoreCase)
                ? new CriterionConformityResult(CriterionOutcome.Conforming, null)
                : new CriterionConformityResult(CriterionOutcome.NonConforming, "Texto diferente do esperado.");
        return new CriterionConformityResult(CriterionOutcome.Inconclusive, $"Operador '{op}' não suportado para TEXT.");
    }

    private static CriterionConformityResult EvaluateNumber(
        string key,
        decimal? value,
        string? answerUnit,
        string? criterionUnit,
        ApprovalCondition? condition)
    {
        // Absent number is NOT zero.
        if (!value.HasValue)
            return new CriterionConformityResult(CriterionOutcome.Unanswered, $"Critério '{key}' sem valor numérico; ausência não representa zero.");

        var expectedUnit = condition?.ExpectedUnit ?? criterionUnit;
        if (!string.IsNullOrWhiteSpace(expectedUnit))
        {
            if (string.IsNullOrWhiteSpace(answerUnit))
                return new CriterionConformityResult(CriterionOutcome.Inconclusive, "Unidade da resposta ausente; comparação numérica não realizada.");
            if (!expectedUnit.Equals(answerUnit, StringComparison.OrdinalIgnoreCase))
                return new CriterionConformityResult(CriterionOutcome.Inconclusive, "Unidade incompatível; comparação numérica não realizada.");
        }

        if (condition is null || string.IsNullOrWhiteSpace(condition.Operator) || !condition.ExpectedNumber.HasValue)
            return new CriterionConformityResult(CriterionOutcome.Inconclusive, "Condição numérica incompleta.");

        var op = condition.Operator.ToUpperInvariant();
        var expected = condition.ExpectedNumber.Value;
        var actual = value.Value;
        var ok = op switch
        {
            "EQ" => actual == expected,
            "NE" => actual != expected,
            "GT" => actual > expected,
            "GTE" => actual >= expected,
            "LT" => actual < expected,
            "LTE" => actual <= expected,
            "BETWEEN" => condition.ExpectedNumberMax.HasValue &&
                         actual >= Math.Min(expected, condition.ExpectedNumberMax.Value) &&
                         actual <= Math.Max(expected, condition.ExpectedNumberMax.Value),
            _ => (bool?)null
        };

        if (ok is null)
            return new CriterionConformityResult(CriterionOutcome.Inconclusive, $"Operador '{op}' não suportado para NUMBER.");
        return ok.Value
            ? new CriterionConformityResult(CriterionOutcome.Conforming, null)
            : new CriterionConformityResult(CriterionOutcome.NonConforming, "Valor numérico fora da condição de aprovação.");
    }

    private static CriterionConformityResult EvaluateDate(string key, DateOnly? value, ApprovalCondition? condition)
    {
        if (!value.HasValue)
            return new CriterionConformityResult(CriterionOutcome.Unanswered, $"Critério '{key}' sem data.");
        if (condition is null || string.IsNullOrWhiteSpace(condition.Operator) || !condition.ExpectedDate.HasValue)
            return new CriterionConformityResult(CriterionOutcome.Inconclusive, "Condição de data incompleta.");

        var op = condition.Operator.ToUpperInvariant();
        var expected = condition.ExpectedDate.Value;
        var actual = value.Value;
        var ok = op switch
        {
            "EQ" => actual == expected,
            "NE" => actual != expected,
            "GT" => actual > expected,
            "GTE" => actual >= expected,
            "LT" => actual < expected,
            "LTE" => actual <= expected,
            "BETWEEN" => condition.ExpectedDateMax.HasValue &&
                         actual >= (expected < condition.ExpectedDateMax.Value ? expected : condition.ExpectedDateMax.Value) &&
                         actual <= (expected > condition.ExpectedDateMax.Value ? expected : condition.ExpectedDateMax.Value),
            _ => (bool?)null
        };
        if (ok is null)
            return new CriterionConformityResult(CriterionOutcome.Inconclusive, $"Operador '{op}' não suportado para DATE.");
        return ok.Value
            ? new CriterionConformityResult(CriterionOutcome.Conforming, null)
            : new CriterionConformityResult(CriterionOutcome.NonConforming, "Data fora da condição de aprovação.");
    }

    private static CriterionConformityResult EvaluateDocument(string key, bool present, ApprovalCondition? condition)
    {
        if (!present)
            return new CriterionConformityResult(CriterionOutcome.Unanswered, $"Critério '{key}' sem evidência documental.");
        var op = condition?.Operator?.ToUpperInvariant() ?? "PRESENT";
        if (op is "PRESENT" or "ANY")
            return new CriterionConformityResult(CriterionOutcome.Conforming, null);
        return new CriterionConformityResult(CriterionOutcome.Inconclusive, $"Operador '{op}' não suportado para DOCUMENT_EVIDENCE.");
    }
}

public enum CriterionOutcome
{
    Conforming,
    NonConforming,
    Inconclusive,
    NotApplicable,
    Unanswered
}

public sealed record ModelSelectionCandidate(Guid ModelId, int Precedence, int SpecificityScore);

public sealed record ModelSelectionResult(
    bool Ambiguous,
    Guid? SelectedModelId,
    IReadOnlyList<Guid> TiedModelIds,
    string Explanation);

/// <summary>Structured approval_condition (JSON-compatible records; no script engine).</summary>
public sealed record ApprovalCondition(
    string? Operator = null,
    bool? ExpectedPass = null,
    string? ExpectedText = null,
    decimal? ExpectedNumber = null,
    decimal? ExpectedNumberMax = null,
    string? ExpectedUnit = null,
    DateOnly? ExpectedDate = null,
    DateOnly? ExpectedDateMax = null,
    IReadOnlyList<string>? ExpectedChoices = null);

public sealed record InspectionCriterionDefinition(
    string StableKey,
    string CriterionType,
    bool Required,
    bool Critical,
    bool AllowNotApplicable,
    bool RequireNaJustification,
    bool RequireReview,
    string? Unit = null,
    IReadOnlyList<string>? Options = null,
    ApprovalCondition? ApprovalCondition = null,
    decimal? Weight = null);

public sealed record InspectionAnswerInput(
    bool IsNotApplicable = false,
    string? NotApplicableJustification = null,
    bool? PassFailValue = null,
    string? TextValue = null,
    decimal? NumberValue = null,
    string? NumberUnit = null,
    DateOnly? DateValue = null,
    IReadOnlyList<string>? ChoiceValues = null,
    Guid? DocumentEvidenceId = null,
    bool HasDocumentEvidence = false);

public sealed record CriterionConformityResult(CriterionOutcome Outcome, string? Reason);

public sealed record InspectionCriterionAnswerState(
    string StableKey,
    bool Required,
    bool Critical,
    bool RequireReview,
    CriterionOutcome Outcome,
    decimal? Weight = null);

public sealed record OverallInspectionResult(
    string Result,
    IReadOnlyList<string> DeterminingStableKeys,
    decimal? WeightedScorePercent,
    string? ScoringNote);
