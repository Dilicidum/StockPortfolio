namespace StockPortfolio.Shared.Kernel;

/// <summary>An amount of money in a single currency.</summary>
public readonly record struct Money
{
    /// <summary>The ISO 4217 code of the only currency the application handles today.</summary>
    public const string UsdCurrencyCode = "USD";

    /// <summary>Creates an amount, upper-casing the currency so equality and the currency guard agree.</summary>
    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    /// <summary>Gets the amount, which is always exact: money is decimal server-side and never double.</summary>
    public decimal Amount { get; }

    /// <summary>Gets the ISO 4217 currency code, always upper-cased.</summary>
    public string Currency { get; }

    /// <summary>Creates an amount in US dollars.</summary>
    public static Money Usd(decimal amount) => new(amount, UsdCurrencyCode);

    /// <summary>Creates a zero amount in the given currency.</summary>
    public static Money Zero(string currency) => new(0m, currency);

    /// <summary>Adds another amount of the same currency.</summary>
    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    /// <summary>Subtracts another amount of the same currency.</summary>
    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    /// <summary>Multiplies the amount by a dimensionless factor, such as a share quantity.</summary>
    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    /// <summary>Adds two amounts of the same currency.</summary>
    public static Money operator +(Money left, Money right) => left.Add(right);

    /// <summary>Subtracts two amounts of the same currency.</summary>
    public static Money operator -(Money left, Money right) => left.Subtract(right);

    /// <summary>Multiplies an amount by a factor.</summary>
    public static Money operator *(Money left, decimal right) => left.Multiply(right);

    private void EnsureSameCurrency(Money other)
    {
        // Ordinal, not OrdinalIgnoreCase: the constructor already upper-cased both, and the generated
        // record equality is ordinal — a looser guard here would make Add non-commutative under equality.
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Cannot combine money in " + Currency + " with money in " + other.Currency + ".");
        }
    }
}
