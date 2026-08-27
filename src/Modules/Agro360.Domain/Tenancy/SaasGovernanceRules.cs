using System.Text.RegularExpressions;

namespace Agro360.Domain.Tenancy;

/// <summary>Invariantes centrais da operacao SaaS. Os casos de uso chamam estas regras antes de persistir.</summary>
public static partial class SaasGovernanceRules
{
    public const decimal UsageWarningPercentage = 80m;

    public static void EnsureStatusTransition(string currentStatus, string targetStatus, string? reason)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IMPLEMENTING", "TRIAL", "ACTIVE", "SUSPENDED", "INACTIVE", "CANCELLED"
        };
        if (!allowed.Contains(currentStatus) || !allowed.Contains(targetStatus))
            throw new ArgumentException("Status de tenant invalido.");
        if (currentStatus.Equals(targetStatus, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O tenant ja possui o status solicitado.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Ativacao, suspensao, inativacao e reativacao exigem motivo.");
    }

    public static void EnsureSubscriptionCanStart(bool planActive, DateOnly startsOn, DateOnly? endsOn, decimal price, decimal discount, string? discountReason)
    {
        if (!planActive) throw new InvalidOperationException("Plano inativo nao pode ser contratado.");
        if (endsOn.HasValue && endsOn.Value <= startsOn) throw new ArgumentException("A data final deve ser posterior a data inicial.");
        if (price < 0 || discount < 0 || discount > price) throw new ArgumentOutOfRangeException(nameof(price), "Valor e desconto devem ser validos.");
        if (discount > 0 && string.IsNullOrWhiteSpace(discountReason)) throw new InvalidOperationException("Desconto exige motivo.");
    }

    public static void EnsureChargeCanChange(string targetStatus, decimal amount, DateOnly? paidOn, string? reason)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "O valor da cobranca deve ser positivo.");
        if (targetStatus.Equals("PAID", StringComparison.OrdinalIgnoreCase) && !paidOn.HasValue)
            throw new InvalidOperationException("A baixa manual exige data de pagamento.");
        if (targetStatus.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("O cancelamento exige motivo.");
    }

    public static UsageDecision EvaluateUsage(long current, long planLimit, long? overrideLimit = null, DateTimeOffset? overrideExpiresAt = null, DateTimeOffset? now = null)
    {
        if (current < 0 || planLimit <= 0) throw new ArgumentOutOfRangeException(nameof(current), "Consumo e limite devem ser validos.");
        var instant = now ?? DateTimeOffset.UtcNow;
        var effective = overrideLimit is > 0 && overrideExpiresAt > instant ? overrideLimit.Value : planLimit;
        var percentage = Math.Round(current * 100m / effective, 2);
        return new UsageDecision(current, effective, percentage, percentage >= UsageWarningPercentage, current >= effective);
    }

    public static bool ResolveFeature(bool planEnabled, bool? tenantOverride, string? origin)
        => origin?.Equals("ADMIN_BLOCK", StringComparison.OrdinalIgnoreCase) == true ? false : tenantOverride ?? planEnabled;

    public static void EnsureOverride(string? reason, DateTimeOffset expiresAt, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Override exige motivo.");
        if (expiresAt <= (now ?? DateTimeOffset.UtcNow)) throw new ArgumentException("Override deve possuir validade futura.");
    }

    public static void EnsureOnboardingCanComplete(IEnumerable<OnboardingStepState> steps)
    {
        var values = steps.ToArray();
        if (values.Length == 0 || values.Any(x => x.Required && !x.Completed))
            throw new InvalidOperationException("Conclua todas as etapas obrigatorias do onboarding.");
    }

    public static void EnsureBranding(string primaryColor, string secondaryColor, string accentColor, string? fileName, long? fileSize)
    {
        if (!HexColor().IsMatch(primaryColor) || !HexColor().IsMatch(secondaryColor) || !HexColor().IsMatch(accentColor))
            throw new ArgumentException("As cores devem usar o formato hexadecimal #RRGGBB.");
        if (fileName is not null && !new[] { ".png", ".jpg", ".jpeg", ".webp", ".svg" }.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Formato de logo nao permitido.");
        if (fileSize is > 2_097_152) throw new ArgumentException("A logo deve possuir no maximo 2 MB.");
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}

public readonly record struct UsageDecision(long Current, long EffectiveLimit, decimal Percentage, bool Warning, bool BlockNewRecords);
public readonly record struct OnboardingStepState(bool Required, bool Completed);
