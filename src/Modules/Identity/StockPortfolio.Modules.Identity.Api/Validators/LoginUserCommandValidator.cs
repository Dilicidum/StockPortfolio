using FluentValidation;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.LoginUser;

namespace StockPortfolio.Modules.Identity.Api.Validators;

/// <summary>Shape rules for LoginUserCommand: both fields must be present, and nothing else.</summary>
public sealed class LoginUserCommandValidator : AbstractValidator<LoginUserCommand>
{
    /// <summary>Builds the rule set.</summary>
    public LoginUserCommandValidator()
    {
        RuleFor(request => request.Email)
            .NotEmpty()
            .WithMessage("Email is required.");

        RuleFor(request => request.Password)
            .NotEmpty()
            .WithMessage("Password is required.");
    }
}
