using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Agro360.Domain.Governance;

public static partial class DataGovernanceRules
{
    public static readonly IReadOnlySet<string> ImportModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "TENANTS", "USERS", "ROLES", "FARMS", "PLOTS", "SEASONS", "PRODUCERS", "HERDS", "INVENTORY", "SUPPLIERS", "PRODUCTS", "CUSTOMERS", "DOCUMENTS" };

    public static string NormalizeDocument(string value)
    {
        var digits = Digits().Replace(value ?? string.Empty, string.Empty);
        if (digits.Length is not (11 or 14) || digits.Distinct().Count() == 1 || !ValidDocumentDigits(digits))
            throw new ArgumentException("CPF/CNPJ inválido.", nameof(value));
        return digits;
    }

    public static string NormalizeEmail(string value)
    {
        try { return new MailAddress(value.Trim()).Address.ToLowerInvariant(); }
        catch { throw new ArgumentException("E-mail inválido.", nameof(value)); }
    }

    public static void ValidateImport(string module, string csv)
    {
        if (!ImportModules.Contains(module)) throw new ArgumentException("Módulo de importação não suportado.", nameof(module));
        if (string.IsNullOrWhiteSpace(csv) || !csv.Contains('\n')) throw new ArgumentException("CSV inválido: inclua cabeçalho e ao menos uma linha.", nameof(csv));
        if (csv.Length > 10_000_000) throw new ArgumentException("CSV excede o limite de 10 MB.", nameof(csv));
    }

    public static void ValidateFindingTransition(string status, string? justification)
    {
        if (status is not ("REVIEWED" or "CORRECTED" or "IGNORED")) throw new ArgumentException("Status de análise inválido.");
        if (string.IsNullOrWhiteSpace(justification)) throw new ArgumentException("A alteração exige justificativa.");
    }

    public static void ValidateLgpdTransition(string status, string? reason)
    {
        string[] valid = ["OPEN", "IN_REVIEW", "AWAITING_VALIDATION", "FULFILLED", "REFUSED", "CANCELLED"];
        if (!valid.Contains(status)) throw new ArgumentException("Status LGPD inválido.");
        if (status == "REFUSED" && string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A recusa LGPD exige motivo.");
    }

    public static string MaskDocument(string? value)
    {
        var digits = Digits().Replace(value ?? string.Empty, string.Empty);
        return digits.Length < 5 ? "***" : $"***.{digits[^4..]}";
    }

    private static bool ValidDocumentDigits(string value) => value.Length == 11
        ? Check(value, 9, [10,9,8,7,6,5,4,3,2]) && Check(value, 10, [11,10,9,8,7,6,5,4,3,2])
        : Check(value, 12, [5,4,3,2,9,8,7,6,5,4,3,2]) && Check(value, 13, [6,5,4,3,2,9,8,7,6,5,4,3,2]);

    private static bool Check(string value, int position, int[] weights)
    {
        var sum = weights.Select((weight, index) => (value[index] - '0') * weight).Sum();
        var digit = 11 - sum % 11;
        if (digit >= 10) digit = 0;
        return value[position] - '0' == digit;
    }

    [GeneratedRegex("[^0-9]")]
    private static partial Regex Digits();
}
