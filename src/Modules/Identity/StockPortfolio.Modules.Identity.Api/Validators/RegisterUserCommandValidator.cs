using FluentValidation;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RegisterUser;

namespace StockPortfolio.Modules.Identity.Api.Validators;

/// <summary>Shape rules for RegisterUserCommand — the only layer of validation that can answer "is this even an.</summary>
public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    /// <summary>The shortest password accepted, in characters.</summary>
    public const int MinimumPasswordLength = 12;

    /// <summary>The longest password accepted, in characters.</summary>
    public const int MaximumPasswordLength = 128;

    /// <summary>The longest email address accepted, in characters — the RFC 5321 path limit.</summary>
    public const int MaximumEmailLength = 254;

    /// <summary>Builds the rule set.</summary>
    public RegisterUserCommandValidator()
    {
        // Cascade.Stop everywhere: an empty password should produce one actionable message, not "required".
        RuleFor(request => request.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Email is required.")
            .MaximumLength(MaximumEmailLength)
            .WithMessage("Email must be {MaxLength} characters or fewer.")
            .EmailAddress()
            .WithMessage("Email must look like name@example.com.");

        RuleFor(request => request.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Password is required.")
            .MinimumLength(MinimumPasswordLength)
            .WithMessage(
                "Password must be at least {MinLength} characters. Length beats punctuation — " +
                "four ordinary words strung together is easier to remember and harder to crack.")
            .MaximumLength(MaximumPasswordLength)
            .WithMessage("Password must be {MaxLength} characters or fewer.");

        // A cross-field rule - precisely what DataAnnotations cannot express.
        RuleFor(request => request.Password)
            .Must((request, password) => !string.Equals(password, request.Email, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Password must not be the same as your email address.")
            .When(request => !string.IsNullOrEmpty(request.Password));
    }
}
