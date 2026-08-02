namespace StockPortfolio.Shared.Kernel;

/// <summary>An amount of money in a single currency.</summary>
public readonly record struct Money(decimal Amount, string Currency)
{
    /// <summary>The ISO 4217 code of the only currency the application handles today.</summary>
    public const string UsdCurrencyCode = "USD";

    /// <summary>Creates an amount in US dollars.</summary>
    public static Money Usd(decimal amount) => new(amount, UsdCurrencyCode);

    /// <summary>Creates a zero amount in the given currency.</summary>
    public static Money Zero(string currency) => new(0m, currency);

    /// <summary>Adds another amount of the same currency.</summary>
    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return this with { Amount = Amount + other.Amount };
    }

    /// <summary>Subtracts another amount of the same currency.</summary>
    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return this with { Amount = Amount - other.Amount };
    }

    /// <summary>Multiplies the amount by a dimensionless factor, such as a share quantity.</summary>
    public Money Multiply(decimal factor) => this with { Amount = Amount * factor };

    /// <summary>Adds two amounts of the same currency.</summary>
    public static Money operator +(Money left, Money right) => left.Add(right);

    /// <summary>Subtracts two amounts of the same currency.</summary>
    public static Money operator -(Money left, Money right) => left.Subtract(right);

    /// <summary>Multiplies an amount by a factor.</summary>
    public static Money operator *(Money left, decimal right) => left.Multiply(right);

    private void EnsureSameCurrency(Money other)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(Currency, other.Currency))
        {
            throw new InvalidOperationException(
                "Cannot combine money in " + Currency + " with money in " + other.Currency + ".");
        }
    }
}
