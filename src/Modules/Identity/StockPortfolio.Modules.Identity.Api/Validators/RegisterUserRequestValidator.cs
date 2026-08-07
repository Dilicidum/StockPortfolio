using FluentValidation;

using StockPortfolio.Modules.Identity.Api.Requests;

namespace StockPortfolio.Modules.Identity.Api.Validators;

public sealed class RegisterUserRequestValidator : AbstractValidator<RegisterUserRequest>
{
    public const int MinimumPasswordLength = 12;

    public RegisterUserRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().EmailAddress().MaximumLength(256);

        RuleFor(request => request.Password).NotEmpty().MinimumLength(MinimumPasswordLength);

        RuleFor(request => request.Password)
            .NotEqual(request => request.Email)
            .WithMessage("The password must not be the email address.");
    }
}
