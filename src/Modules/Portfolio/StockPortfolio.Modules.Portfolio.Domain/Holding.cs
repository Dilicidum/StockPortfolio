using System.Globalization;
using OneOf;
using OneOf.Types;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Portfolio.Domain;

/// <summary>One user's position in one ticker. A unique index on (user_id, ticker) keeps it one row.</summary>
public sealed class Holding
{
    /// <summary>One unit of the column's precision; below this a quantity rounds to zero on store.</summary>
    private const decimal MinimumQuantity = 0.000001m;

    /// <summary>The largest value numeric(18,6) holds; above it Postgres raises 22003 rather than storing.</summary>
    private const decimal MaximumStorableValue = 999999999999.999999m;

    /// <summary>Decimal places every stored number is rounded to, matching numeric(18,6).</summary>
    private const int StoredScale = 6;

    /// <summary>The only constructor. Assigns and nothing else; EF binds it by name for every row.</summary>
    private Holding(
        HoldingId id,
        Guid userId,
        Ticker ticker,
        decimal quantity,
        bool isVisible,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        UserId = userId;
        Ticker = ticker;
        Quantity = quantity;
        IsVisible = isVisible;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Gets the identity of the holding.</summary>
    public HoldingId Id { get; private set; }

    /// <summary>Gets the owning user. A plain Guid: Portfolio does not own the Identity module's UserId.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Gets the symbol held, already upper-cased by Ticker.Create.</summary>
    public Ticker Ticker { get; private set; }

    /// <summary>Gets the number of shares, which may be fractional but never finer than the stored scale.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets the weighted average purchase price. Omitted from the constructor — efcore#31621.</summary>
    public Money AveragePrice { get; private set; }

    /// <summary>Gets whether the dashboard shows this position. Always true until Phase 5 adds the toggle.</summary>
    public bool IsVisible { get; private set; }

    /// <summary>Gets the instant the position was opened.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets the instant the position last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The ceiling as every message spells it, so no two call sites word it differently.</summary>
    private static string MaximumStorableValueText =>
        MaximumStorableValue.ToString("0.000000", CultureInfo.InvariantCulture);

    /// <summary>Opens a position. The only way to build a Holding.</summary>
    public static OneOf<Holding, InvalidInput> Create(
        Guid userId,
        Ticker ticker,
        decimal quantity,
        Money purchasePrice,
        TimeProvider clock)
    {
        // Rounded before validation, so the rules judge the number the column will actually hold.
        var storedQuantity = ToStoredScale(quantity);
        var storedPrice = ToStoredScale(purchasePrice);

        return ValidateAmounts(storedQuantity, storedPrice).Match(
            valid => Open(userId, ticker, storedQuantity, storedPrice, clock),
            invalid => invalid);
    }

    /// <summary>Builds the position, once the numbers it is built from have passed.</summary>
    private static OneOf<Holding, InvalidInput> Open(
        Guid userId,
        Ticker ticker,
        decimal storedQuantity,
        Money storedPrice,
        TimeProvider clock)
    {
        var now = clock.GetUtcNow();

        var holding = new Holding(HoldingId.New(), userId, ticker, storedQuantity, isVisible: true, now, now);

        // Assigned after construction because a complex type cannot be a constructor parameter
        // (efcore#31621, open and pushed back to Backlog). private set is reachable from inside the type.
        holding.AveragePrice = storedPrice;

        return holding;
    }

    /// <summary>Merges a further purchase: quantities sum, price becomes the weighted average.</summary>
    public OneOf<Success, InvalidInput> Merge(decimal quantity, Money purchasePrice, TimeProvider clock)
    {
        var storedQuantity = ToStoredScale(quantity);
        var storedPrice = ToStoredScale(purchasePrice);

        return ValidateAmountsAndCurrency(storedQuantity, storedPrice).Match(
            valid => ApplyMerge(storedQuantity, storedPrice, clock),
            invalid => invalid);
    }

    /// <summary>Merges amounts that have already passed; only the summed quantity is still in question.</summary>
    private OneOf<Success, InvalidInput> ApplyMerge(decimal storedQuantity, Money storedPrice, TimeProvider clock)
    {
        // Both operands already sit at the stored scale, so their sum is exact — but a sum of two
        // individually legal quantities can still cross the ceiling each of them cleared alone.
        var total = Quantity + storedQuantity;

        if (total > MaximumStorableValue)
        {
            var totalText = total.ToString("0.000000", CultureInfo.InvariantCulture);

            return new InvalidInput(
                "quantity",
                $"This purchase would take the position to {totalText} shares, "
                + $"and a position holds at most {MaximumStorableValueText}.");
        }

        // .Amount arithmetic, not Money's operators: Money has no division, and Add would throw on a
        // currency mismatch that ValidateAmountsAndCurrency has already turned into a result case.
        var weighted = ((AveragePrice.Amount * Quantity) + (storedPrice.Amount * storedQuantity)) / total;

        Quantity = total;
        AveragePrice = new Money(ToStoredScale(weighted), AveragePrice.Currency);
        UpdatedAt = clock.GetUtcNow();

        return new Success();
    }

    /// <summary>Corrects a mistyped entry. Replaces, never averages — a typo is not a second purchase.</summary>
    public OneOf<Success, InvalidInput> Correct(decimal quantity, Money purchasePrice, TimeProvider clock)
    {
        var storedQuantity = ToStoredScale(quantity);
        var storedPrice = ToStoredScale(purchasePrice);

        return ValidateAmountsAndCurrency(storedQuantity, storedPrice).Match(
            valid => ApplyCorrection(storedQuantity, storedPrice, clock),
            invalid => invalid);
    }

    /// <summary>Replaces the position's numbers, which by here have already passed.</summary>
    private OneOf<Success, InvalidInput> ApplyCorrection(
        decimal storedQuantity,
        Money storedPrice,
        TimeProvider clock)
    {
        Quantity = storedQuantity;
        AveragePrice = storedPrice;
        UpdatedAt = clock.GetUtcNow();

        return new Success();
    }

    // Hiding is a display filter: it changes no figure and no alert.
    public void SetVisibility(bool isVisible, TimeProvider clock)
    {
        IsVisible = isVisible;
        UpdatedAt = clock.GetUtcNow();
    }

    /// <summary>The single place a number is rounded, so the entity and the column can never disagree.</summary>
    private static decimal ToStoredScale(decimal value) =>
        Math.Round(value, StoredScale, MidpointRounding.ToEven);

    /// <summary>Rounds the amount of a price, leaving its currency alone.</summary>
    private static Money ToStoredScale(Money price) => new(ToStoredScale(price.Amount), price.Currency);

    /// <summary>Validates the amounts and, on top of that, that the currency matches this position's.</summary>
    private OneOf<Success, InvalidInput> ValidateAmountsAndCurrency(decimal quantity, Money purchasePrice)
    {
        // Checked BEFORE any Money arithmetic: EnsureSameCurrency throws, and a throw here would
        // surface as a 500 instead of the 400 this rule is meant to produce.
        if (!string.Equals(purchasePrice.Currency, AveragePrice.Currency, StringComparison.Ordinal))
        {
            return new InvalidInput(
                "price",
                $"This position is held in {AveragePrice.Currency}; {purchasePrice.Currency} cannot be mixed in.");
        }

        return ValidateAmounts(quantity, purchasePrice);
    }

    /// <summary>Validates the amounts alone — the rules that hold whether or not a position exists.</summary>
    private static OneOf<Success, InvalidInput> ValidateAmounts(decimal quantity, Money purchasePrice)
    {
        if (quantity < MinimumQuantity)
        {
            return new InvalidInput(
                "quantity",
                $"Quantity must be at least {MinimumQuantity.ToString("0.000000", CultureInfo.InvariantCulture)}.");
        }

        if (quantity > MaximumStorableValue)
        {
            return new InvalidInput("quantity", $"Quantity must be at most {MaximumStorableValueText}.");
        }

        if (purchasePrice.Amount <= 0m)
        {
            return new InvalidInput("price", "Purchase price must be greater than zero.");
        }

        if (purchasePrice.Amount > MaximumStorableValue)
        {
            return new InvalidInput("price", $"Purchase price must be at most {MaximumStorableValueText}.");
        }

        return new Success();
    }
}
