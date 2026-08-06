using FluentValidation;

using StockPortfolio.Modules.MarketData.Api.Requests;

namespace StockPortfolio.Modules.MarketData.Api.Validators;

/// <summary>Shape rules for SaveApiKeyRequest. Nothing about the format: the provider is the authority
/// on what a valid key looks like, and a guessed regex here would reject keys that work.</summary>
public sealed class SaveApiKeyRequestValidator : AbstractValidator<SaveApiKeyRequest>
{
    /// <summary>Generous on purpose — a real provider key is well under this, but this layer does not guess.</summary>
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
