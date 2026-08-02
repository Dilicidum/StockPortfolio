using FluentValidation;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RegisterUser;

namespace StockPortfolio.Modules.Identity.Api.Validators;

/// <summary>
/// Shape rules for <see cref="RegisterUserCommand"/> — the only layer of validation that can answer
/// "is this even an email?" and "is this password long enough?" without touching the database.
/// </summary>
/// <remarks>
/// <para>
/// <b>Password policy, and why it is length-only.</b> The floor is
/// <see cref="MinimumPasswordLength"/> characters with a ceiling of
/// <see cref="MaximumPasswordLength"/>, and there are deliberately <i>no</i> character-class
/// rules. NIST SP 800-63B is explicit that composition rules ("one upper, one digit, one symbol")
/// make passwords weaker in practice: users satisfy them with predictable transforms —
/// <c>Password1!</c>, <c>P@ssw0rd</c> — which is exactly the space a cracking dictionary
/// enumerates first. Length is the lever that actually buys entropy, so this validator spends its
/// only requirement there. The floor sits above the 8-character minimum 800-63B mandates and
/// below the 15 it recommends: 15 is the right call when a breached-password corpus is also
/// checked, and this validator cannot check one, because that is I/O and a validator does none.
/// Twelve is the honest middle for a service whose only defence is length.
/// </para>
/// <para>
/// The ceiling is not a security rule, it is a denial-of-service one: passwords are hashed with
/// Argon2id, whose cost grows with input, so unbounded input is a free CPU amplifier. It is well
/// clear of the 64 characters 800-63B requires be accepted, so no realistic passphrase hits it.
/// </para>
/// <para>
/// <b>What is not here.</b> "Is this email already taken?" is a <i>context</i> question — it needs
/// the database, the answer can change between the check and the insert, and the unique index is
/// the only real guarantee. The handler answers it as an <c>EmailAlreadyUsed</c> result case
/// mapped to <c>409</c>, never as a <c>400</c> from here.
/// </para>
/// </remarks>
public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    /// <summary>The shortest password accepted, in characters.</summary>
    public const int MinimumPasswordLength = 12;

    /// <summary>The longest password accepted, in characters. A hashing-cost guard, not a security rule.</summary>
    public const int MaximumPasswordLength = 128;

    /// <summary>The longest email address accepted, in characters — the RFC 5321 path limit.</summary>
    public const int MaximumEmailLength = 254;

    /// <summary>Builds the rule set.</summary>
    public RegisterUserCommandValidator()
    {
        // Cascade.Stop everywhere: an empty password should produce one actionable message,
        // not "required" and "too short" stacked on the same field.
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

        // A cross-field rule, and the reason this is FluentValidation rather than the built-in
        // DataAnnotations-driven AddValidation(): an attribute on Password cannot see Email.
        RuleFor(request => request.Password)
            .Must((request, password) => !string.Equals(password, request.Email, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Password must not be the same as your email address.")
            .When(request => !string.IsNullOrEmpty(request.Password));
    }
}
