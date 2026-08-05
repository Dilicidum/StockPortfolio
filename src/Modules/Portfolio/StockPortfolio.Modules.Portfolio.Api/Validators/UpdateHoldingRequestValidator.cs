using FluentValidation;
using StockPortfolio.Modules.Portfolio.Api.Requests;

namespace StockPortfolio.Modules.Portfolio.Api.Validators;

/// <summary>Shape rules for UpdateHoldingRequest — the same two numeric rules, without a ticker.</summary>
public sealed class UpdateHoldingRequestValidator : AbstractValidator<UpdateHoldingRequest>
{
    /// <summary>Builds the rule set.</summary>
    public UpdateHoldingRequestValidator()
    {
        RuleFor(request => request.Quantity)
            .Cascade(CascadeMode.Stop)
            .GreaterThanOrEqualTo(AddHoldingRequestValidator.MinimumQuantity)
            .WithMessage("Quantity must be at least 0.000001.")
            .LessThanOrEqualTo(AddHoldingRequestValidator.MaximumStorableValue)
            .WithMessage("Quantity must be at most 999999999999.999999.");

        RuleFor(request => request.Price)
            .Cascade(CascadeMode.Stop)
            .GreaterThan(0m)
            .WithMessage("Price must be greater than zero.")
            .LessThanOrEqualTo(AddHoldingRequestValidator.MaximumStorableValue)
            .WithMessage("Price must be at most 999999999999.999999.");
    }
}
