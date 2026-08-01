namespace StockPortfolio.Shared.Kernel;

/// <summary>
/// An amount of money in a single currency. <see cref="decimal"/> server-side, never a
/// floating-point type, and never computed in the browser.
/// </summary>
/// <param name="Amount">The amount, in the major unit of <paramref name="Currency"/>.</param>
/// <param name="Currency">The ISO 4217 alphabetic code, for example <c>USD</c>.</param>
public readonly record struct Money(decimal Amount, string Currency)
{
    /// <summary>The ISO 4217 code of the only currency the application handles today.</summary>
    public const string UsdCurrencyCode = "USD";

    /// <summary>Creates an amount in US dollars.</summary>
    /// <param name="amount">The amount in dollars.</param>
    /// <returns>The amount, tagged <c>USD</c>.</returns>
    public static Money Usd(decimal amount) => new(amount, UsdCurrencyCode);

    /// <summary>Creates a zero amount in the given currency.</summary>
    /// <param name="currency">The ISO 4217 alphabetic code.</param>
    /// <returns>Zero, tagged <paramref name="currency"/>.</returns>
    public static Money Zero(string currency) => new(0m, currency);

    /// <summary>Adds another amount of the same currency.</summary>
    /// <param name="other">The amount to add.</param>
    /// <returns>The sum, in the same currency.</returns>
    /// <exception cref="InvalidOperationException">The currencies differ.</exception>
    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return this with { Amount = Amount + other.Amount };
    }

    /// <summary>Subtracts another amount of the same currency.</summary>
    /// <param name="other">The amount to subtract.</param>
    /// <returns>The difference, in the same currency.</returns>
    /// <exception cref="InvalidOperationException">The currencies differ.</exception>
    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return this with { Amount = Amount - other.Amount };
    }

    /// <summary>Multiplies the amount by a dimensionless factor, such as a share quantity.</summary>
    /// <param name="factor">The factor to multiply by.</param>
    /// <returns>The product, in the same currency.</returns>
    public Money Multiply(decimal factor) => this with { Amount = Amount * factor };

    /// <summary>Adds two amounts of the same currency. Named alternate: <see cref="Add"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The sum, in the same currency.</returns>
    /// <exception cref="InvalidOperationException">The currencies differ.</exception>
    public static Money operator +(Money left, Money right) => left.Add(right);

    /// <summary>Subtracts two amounts of the same currency. Named alternate: <see cref="Subtract"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The difference, in the same currency.</returns>
    /// <exception cref="InvalidOperationException">The currencies differ.</exception>
    public static Money operator -(Money left, Money right) => left.Subtract(right);

    /// <summary>Multiplies an amount by a factor. Named alternate: <see cref="Multiply"/>.</summary>
    /// <param name="left">The amount.</param>
    /// <param name="right">The factor.</param>
    /// <returns>The product, in the same currency.</returns>
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
