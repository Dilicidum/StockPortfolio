using FluentValidation;
using StockPortfolio.Modules.Identity.Api.Requests;

namespace StockPortfolio.Modules.Identity.Api.Validators;

/// <summary>Shape rules for LoginUserRequest: both fields must be present, and nothing else.</summary>
public sealed class LoginUserRequestValidator : AbstractValidator<LoginUserRequest>
{
    /// <summary>Builds the rule set.</summary>
    public LoginUserRequestValidator()
    {
        RuleFor(request => request.Email)
            .NotEmpty()
            .WithMessage("Email is required.");

        RuleFor(request => request.Password)
            .NotEmpty()
            .WithMessage("Password is required.");
    }
}
