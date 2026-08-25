namespace Agro360.SharedKernel;

public static class Guard
{
    public static Guid Required(Guid value, string field) => value == Guid.Empty
        ? throw new DomainException($"{field} é obrigatório.", $"{field}.required")
        : value;

    public static string Required(string? value, string field, int maxLength = 250)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{field} é obrigatório.", $"{field}.required");
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new DomainException($"{field} deve possuir no máximo {maxLength} caracteres.", $"{field}.max_length");
        }

        return normalized;
    }

    public static decimal Positive(decimal value, string field) => value <= 0
        ? throw new DomainException($"{field} deve ser maior que zero.", $"{field}.positive")
        : value;

    public static decimal NonNegative(decimal value, string field) => value < 0
        ? throw new DomainException($"{field} não pode ser negativo.", $"{field}.non_negative")
        : value;

    public static DateOnly Range(DateOnly value, DateOnly minimum, DateOnly maximum, string field) =>
        value < minimum || value > maximum
            ? throw new DomainException($"{field} está fora do intervalo permitido.", $"{field}.range")
            : value;
}
