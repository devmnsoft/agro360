using System.Globalization;
using System.Net.Mail;

namespace Agro360.SharedKernel;

/// <summary>Canonical server-side validation and normalization used by form commands.</summary>
public static class FormValidation
{
    public static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
    public static string NormalizeEmail(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValidEmail(string? value)
    {
        var normalized = NormalizeEmail(value);
        if (normalized.Length is 0 or > 254) return false;
        try { return new MailAddress(normalized).Address == normalized && normalized.Contains('.'); }
        catch (FormatException) { return false; }
    }

    public static bool IsValidCpf(string? value)
    {
        var number = Digits(value);
        if (number.Length != 11 || number.Distinct().Count() == 1) return false;
        return CheckDigit(number, 9, 10) == number[9] - '0' && CheckDigit(number, 10, 11) == number[10] - '0';
    }

    public static bool IsValidCnpj(string? value)
    {
        var number = Digits(value);
        if (number.Length != 14 || number.Distinct().Count() == 1) return false;
        int Digit(int length)
        {
            var sum = 0; var weight = length - 7;
            for (var i = 0; i < length; i++) { sum += (number[i] - '0') * weight--; if (weight == 1) weight = 9; }
            var remainder = sum % 11; return remainder < 2 ? 0 : 11 - remainder;
        }
        return Digit(12) == number[12] - '0' && Digit(13) == number[13] - '0';
    }

    public static bool TryParseDecimal(string? value, CultureInfo culture, out decimal result) =>
        decimal.TryParse(value, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, culture, out result);

    public static bool IsValidDateRange(DateOnly start, DateOnly? end) => end is null || end >= start;

    private static int CheckDigit(string number, int length, int initialWeight)
    {
        var sum = 0;
        for (var i = 0; i < length; i++) sum += (number[i] - '0') * (initialWeight - i);
        var remainder = sum % 11; return remainder < 2 ? 0 : 11 - remainder;
    }
}
