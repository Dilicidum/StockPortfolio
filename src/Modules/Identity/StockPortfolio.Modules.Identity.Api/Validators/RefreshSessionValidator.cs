using FluentValidation;
using StockPortfolio.Modules.Identity.Application.Refresh;

namespace StockPortfolio.Modules.Identity.Api.Validators;

/// <summary>
/// Shape rules for <see cref="RefreshSession"/>: the token must be present and of a plausible size.
/// </summary>
/// <remarks>
/// <para>
/// A refresh token is 32 random bytes rendered base64url — 43 characters. The ceiling is a
/// generous multiple of that, and exists so an oversized body is rejected before it reaches the
/// SHA-256 hash and the database lookup. It is a cost guard, not a format check: validating the
/// exact alphabet or length here would let a caller distinguish "malformed" from "unknown", and
/// the whole point of the endpoint's single <c>401</c> is that it distinguishes neither.
/// </para>
/// <para>
/// Whether the token is real, unexpired and unsuperseded is a <i>context</i> question answered by
/// the handler as an <c>InvalidOrExpired</c> result case mapped to <c>401</c>.
/// </para>
/// </remarks>
public sealed class RefreshSessionValidator : AbstractValidator<RefreshSession>
{
    /// <summary>The longest refresh token accepted, in characters.</summary>
    public const int MaximumRefreshTokenLength = 256;

    /// <summary>Builds the rule set.</summary>
    public RefreshSessionValidator()
    {
        RuleFor(request => request.RefreshToken)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Refresh token is required.")
            .MaximumLength(MaximumRefreshTokenLength)
            .WithMessage("Refresh token must be {MaxLength} characters or fewer.");
    }
}
