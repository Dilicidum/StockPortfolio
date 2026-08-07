using FluentValidation;

using StockPortfolio.Modules.Identity.Api.Requests;

namespace StockPortfolio.Modules.Identity.Api.Validators;

public sealed class LoginUserRequestValidator : AbstractValidator<LoginUserRequest>
{
    public LoginUserRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().MaximumLength(256);
        RuleFor(request => request.Password).NotEmpty();
    }
}
