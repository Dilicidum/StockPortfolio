using FluentValidation;
using StockPortfolio.Modules.Portfolio.Api.Requests;

namespace StockPortfolio.Modules.Portfolio.Api.Validators;

public sealed class AddHoldingRequestValidator : AbstractValidator<AddHoldingRequest>
{
    public const decimal MinimumQuantity = 0.000001m;

    public const decimal MaximumStorableValue = 999999999999.999999m;

    public AddHoldingRequestValidator()
    {
        RuleFor(request => request.Ticker)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("A ticker is required.")
            .Matches("^[A-Za-z]{1,5}$")
            .WithMessage("A ticker is 1 to 5 letters, A to Z.");

        // The ceiling is repeated from the entity so an over-large number is a 400, not a 22003 overflow 500.
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
