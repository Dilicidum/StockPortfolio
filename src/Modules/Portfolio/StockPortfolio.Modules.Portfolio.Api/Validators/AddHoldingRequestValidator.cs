using FluentValidation;
using StockPortfolio.Modules.Portfolio.Api.Requests;

namespace StockPortfolio.Modules.Portfolio.Api.Validators;

/// <summary>Shape rules for AddHoldingRequest — is this even a ticker, a quantity, a price? No I/O.</summary>
public sealed class AddHoldingRequestValidator : AbstractValidator<AddHoldingRequest>
{
    /// <summary>The smallest quantity that survives numeric(18,6); below it a position rounds to zero.</summary>
    public const decimal MinimumQuantity = 0.000001m;

    /// <summary>The largest value numeric(18,6) holds; without this the INSERT fails as a bare 500.</summary>
    public const decimal MaximumStorableValue = 999999999999.999999m;

    /// <summary>Builds the rule set.</summary>
    public AddHoldingRequestValidator()
    {
        // Either case is accepted and Ticker.Create upper-cases: rejecting "aapl" would be rejecting a
        // correct request for looking untidy.
        RuleFor(request => request.Ticker)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("A ticker is required.")
            .Matches("^[A-Za-z]{1,5}$")
            .WithMessage("A ticker is 1 to 5 letters, A to Z.");

        // The ceiling is here as well as in the entity so an over-large number is a 400 naming the
        // field rather than the 22003 numeric-field-overflow the INSERT would otherwise raise.
        RuleFor(request => request.Quantity)
            .Cascade(CascadeMode.Stop)
            .GreaterThanOrEqualTo(MinimumQuantity)
            .WithMessage("Quantity must be at least 0.000001.")
            .LessThanOrEqualTo(MaximumStorableValue)
            .WithMessage("Quantity must be at most 999999999999.999999.");

        RuleFor(request => request.Price)
            .Cascade(CascadeMode.Stop)
            .GreaterThan(0m)
            .WithMessage("Purchase price must be greater than zero.")
            .LessThanOrEqualTo(MaximumStorableValue)
            .WithMessage("Purchase price must be at most 999999999999.999999.");
    }
}
