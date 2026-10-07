namespace Agro360.SharedKernel;

public static class CsvSanitizer
{
    private static readonly char[] FormulaTriggers = ['=', '+', '-', '@', '\t', '\r'];

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value;
        if (StartsWithFormulaTrigger(text))
        {
            text = "'" + text;
        }

        var needsQuotes = text.Contains(';') || text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r');
        if (needsQuotes)
        {
            return $"\"{text.Replace("\"", "\"\"")}\"";
        }

        return text;
    }

    public static string SanitizePlain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value;
        if (StartsWithFormulaTrigger(text))
        {
            text = "'" + text;
        }

        return text.Replace(';', ',');
    }

    private static bool StartsWithFormulaTrigger(string text)
    {
        if (FormulaTriggers.Contains(text[0]))
            return true;

        var index = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;

        return index < text.Length && FormulaTriggers.Contains(text[index]);
    }
}
