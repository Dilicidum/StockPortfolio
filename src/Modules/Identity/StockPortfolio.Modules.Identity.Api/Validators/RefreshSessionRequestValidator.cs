using FluentValidation;
using StockPortfolio.Modules.Identity.Api.Requests;

namespace StockPortfolio.Modules.Identity.Api.Validators;

/// <summary>Shape rules for RefreshSessionRequest: the token must be present and of a plausible size.</summary>
public sealed class RefreshSessionRequestValidator : AbstractValidator<RefreshSessionRequest>
{
    /// <summary>The longest refresh token accepted, in characters.</summary>
    public const int MaximumRefreshTokenLength = 256;

    /// <summary>Builds the rule set.</summary>
    public RefreshSessionRequestValidator()
    {
        RuleFor(request => request.RefreshToken)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Refresh token is required.")
            .MaximumLength(MaximumRefreshTokenLength)
            .WithMessage("Refresh token must be {MaxLength} characters or fewer.");
    }
}
