using FluentValidation;

using StockPortfolio.Modules.Identity.Api.Requests;

namespace StockPortfolio.Modules.Identity.Api.Validators;

public sealed class RefreshSessionRequestValidator : AbstractValidator<RefreshSessionRequest>
{
    public RefreshSessionRequestValidator()
    {
        RuleFor(request => request.RefreshToken).NotEmpty();
    }
}
