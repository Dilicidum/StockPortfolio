namespace StockPortfolio.Shared.Kernel;

public readonly record struct Money
{
    public const string UsdCurrencyCode = "USD";

    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Usd(decimal amount) => new(amount, UsdCurrencyCode);

    public static Money Zero(string currency) => new(0m, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator *(Money left, decimal right) => left.Multiply(right);

    private void EnsureSameCurrency(Money other)
    {
        // Ordinal, not OrdinalIgnoreCase: the constructor upper-cased both, and record equality is ordinal too.
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Cannot combine money in " + Currency + " with money in " + other.Currency + ".");
        }
    }
}
