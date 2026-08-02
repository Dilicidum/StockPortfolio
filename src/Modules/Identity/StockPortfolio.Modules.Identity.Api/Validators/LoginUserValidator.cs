using FluentValidation;
using StockPortfolio.Modules.Identity.Application.Login;

namespace StockPortfolio.Modules.Identity.Api.Validators;

/// <summary>
/// Shape rules for <see cref="LoginUser"/>: both fields must be present, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// This is intentionally far weaker than <see cref="RegisterUserValidator"/>, and the
/// asymmetry is the point. Login must never let the shape of the request tell the caller anything
/// about the account. Applying the registration password policy here would answer "does an
/// account with a 6-character password exist?" with a <c>400</c> instead of a <c>401</c> — and it
/// would lock out every account created before a future policy change, which is a support
/// problem, not a security win.
/// </para>
/// <para>
/// Email shape is not checked either: an address that cannot parse simply matches no account, and
/// the handler already answers that with the same undifferentiated <c>401</c> it gives a wrong
/// password. Presence is the only genuine shape concern — an absent field is a malformed request,
/// not a failed sign-in.
/// </para>
/// </remarks>
public sealed class LoginUserValidator : AbstractValidator<LoginUser>
{
    /// <summary>Builds the rule set.</summary>
    public LoginUserValidator()
    {
        RuleFor(request => request.Email)
            .NotEmpty()
            .WithMessage("Email is required.");

        RuleFor(request => request.Password)
            .NotEmpty()
            .WithMessage("Password is required.");
    }
}
