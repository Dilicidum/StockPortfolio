using FluentValidation;

using StockPortfolio.Modules.MarketData.Api.Requests;

namespace StockPortfolio.Modules.MarketData.Api.Validators;

public sealed class SaveApiKeyRequestValidator : AbstractValidator<SaveApiKeyRequest>
{
    public const int MaximumKeyLength = 256;

    public SaveApiKeyRequestValidator()
    {
        RuleFor(request => request.ApiKey)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("An API key is required.")
            .MaximumLength(MaximumKeyLength)
            .WithMessage("API key must be {MaxLength} characters or fewer.");
    }
}
