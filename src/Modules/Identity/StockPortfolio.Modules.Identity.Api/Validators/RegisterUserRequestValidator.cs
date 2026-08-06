using FluentValidation;

using StockPortfolio.Modules.Identity.Api.Requests;

namespace StockPortfolio.Modules.Identity.Api.Validators;

/// <summary>Shape only. Whether the address is taken is a context question the endpoint asks.</summary>
public sealed class RegisterUserRequestValidator : AbstractValidator<RegisterUserRequest>
{
    /// <summary>Mirrors IdentityOptions.Password.RequiredLength, set in the host.</summary>
    public const int MinimumPasswordLength = 12;

    public RegisterUserRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().EmailAddress().MaximumLength(256);

        RuleFor(request => request.Password).NotEmpty().MinimumLength(MinimumPasswordLength);

        // Cross-field, which DataAnnotations cannot express: the password must not be the address.
        RuleFor(request => request.Password)
            .NotEqual(request => request.Email)
            .WithMessage("The password must not be the email address.");
    }
}
