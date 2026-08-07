using System.Globalization;
using OneOf;
using OneOf.Types;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Portfolio.Domain;

public sealed class Holding
{
    private const decimal MinimumQuantity = 0.000001m;

    private const decimal MaximumStorableValue = 999999999999.999999m;

    private const int StoredScale = 6;

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

    public HoldingId Id { get; private set; }

    public Guid UserId { get; private set; }

    public Ticker Ticker { get; private set; }

    public decimal Quantity { get; private set; }

    public Money AveragePrice { get; private set; }

    public bool IsVisible { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private static string MaximumStorableValueText =>
        MaximumStorableValue.ToString("0.000000", CultureInfo.InvariantCulture);

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

    private static OneOf<Holding, InvalidInput> Open(
        Guid userId,
        Ticker ticker,
        decimal storedQuantity,
        Money storedPrice,
        TimeProvider clock)
    {
        var now = clock.GetUtcNow();

        var holding = new Holding(HoldingId.New(), userId, ticker, storedQuantity, isVisible: true, now, now);

        // Assigned after construction because a complex type cannot be a constructor parameter (efcore#31621).
        holding.AveragePrice = storedPrice;

        return holding;
    }

    public OneOf<Success, InvalidInput> Merge(decimal quantity, Money purchasePrice, TimeProvider clock)
    {
        var storedQuantity = ToStoredScale(quantity);
        var storedPrice = ToStoredScale(purchasePrice);

        return ValidateAmountsAndCurrency(storedQuantity, storedPrice).Match(
            valid => ApplyMerge(storedQuantity, storedPrice, clock),
            invalid => invalid);
    }

    private OneOf<Success, InvalidInput> ApplyMerge(decimal storedQuantity, Money storedPrice, TimeProvider clock)
    {
        var total = Quantity + storedQuantity;

        if (total > MaximumStorableValue)
        {
            var totalText = total.ToString("0.000000", CultureInfo.InvariantCulture);

            return new InvalidInput(
                "quantity",
                $"This purchase would take the position to {totalText} shares, "
                + $"and a position holds at most {MaximumStorableValueText}.");
        }

        var weighted = ((AveragePrice.Amount * Quantity) + (storedPrice.Amount * storedQuantity)) / total;

        Quantity = total;
        AveragePrice = new Money(ToStoredScale(weighted), AveragePrice.Currency);
        UpdatedAt = clock.GetUtcNow();

        return new Success();
    }

    public OneOf<Success, InvalidInput> Correct(decimal quantity, Money purchasePrice, TimeProvider clock)
    {
        var storedQuantity = ToStoredScale(quantity);
        var storedPrice = ToStoredScale(purchasePrice);

        return ValidateAmountsAndCurrency(storedQuantity, storedPrice).Match(
            valid => ApplyCorrection(storedQuantity, storedPrice, clock),
            invalid => invalid);
    }

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

    public void SetVisibility(bool isVisible, TimeProvider clock)
    {
        IsVisible = isVisible;
        UpdatedAt = clock.GetUtcNow();
    }

    private static decimal ToStoredScale(decimal value) =>
        Math.Round(value, StoredScale, MidpointRounding.ToEven);

    private static Money ToStoredScale(Money price) => new(ToStoredScale(price.Amount), price.Currency);

    private OneOf<Success, InvalidInput> ValidateAmountsAndCurrency(decimal quantity, Money purchasePrice)
    {
        // Checked before any Money arithmetic: EnsureSameCurrency throws, and a throw here would be a 500.
        if (!string.Equals(purchasePrice.Currency, AveragePrice.Currency, StringComparison.Ordinal))
        {
            return new InvalidInput(
                "price",
                $"This position is held in {AveragePrice.Currency}; {purchasePrice.Currency} cannot be mixed in.");
        }

        return ValidateAmounts(quantity, purchasePrice);
    }

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
