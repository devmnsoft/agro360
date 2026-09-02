using System.Globalization;

namespace Agro360.SharedKernel;

public readonly record struct Money
{
    public Money(decimal amount, string currency = "BRL")
    {
        Amount = decimal.Round(amount, 4, MidpointRounding.ToEven);
        Currency = Guard.Required(currency, nameof(currency), 3).ToUpperInvariant();
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Zero(string currency = "BRL") => new(0, currency);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator *(Money money, decimal factor) => new(money.Amount * factor, money.Currency);

    public override string ToString() => string.Create(
        CultureInfo.GetCultureInfo("pt-BR"),
        $"{Currency} {Amount:N2}");

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (!string.Equals(left.Currency, right.Currency, StringComparison.Ordinal))
        {
            throw new DomainException("Não é possível operar valores em moedas diferentes.", "money.currency_mismatch");
        }
    }
}

public readonly record struct Quantity
{
    public Quantity(decimal value, string unit)
    {
        Value = Guard.NonNegative(value, nameof(value));
        Unit = Guard.Required(unit, nameof(unit), 16).ToLowerInvariant();
    }

    public decimal Value { get; }

    public string Unit { get; }
}
