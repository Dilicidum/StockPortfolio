using Shouldly;
using StockPortfolio.Modules.Identity.Api;
using StockPortfolio.Modules.Identity.Api.Validators;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RegisterUser;

namespace StockPortfolio.Modules.Identity.UnitTests;

/// <summary>The shape layer of registration.</summary>
public sealed class RegisterUserCommandValidatorTests
{
    private static readonly string ValidPassword = new('a', RegisterUserCommandValidator.MinimumPasswordLength);

    private readonly RegisterUserCommandValidator _validator = new();

    [Fact]
    public void Validate_MalformedEmail_Fails()
    {
        var result = _validator.Validate(new RegisterUserCommand("not-an-email", ValidPassword));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_MalformedEmail_NamesTheEmailField()
    {
        var result = _validator.Validate(new RegisterUserCommand("not-an-email", ValidPassword));

        // The field name is the load-bearing part: ValidationFilter turns Errors into the `errors` dictionary.
        var failure = result.Errors.ShouldHaveSingleItem();
        failure.PropertyName.ShouldBe(nameof(RegisterUserCommand.Email));
        failure.ErrorMessage.ShouldContain("Email");
    }

    [Fact]
    public void Validate_PasswordShorterThanTheFloor_FailsNamingThePasswordField()
    {
        var tooShort = new string('a', RegisterUserCommandValidator.MinimumPasswordLength - 1);

        var result = _validator.Validate(new RegisterUserCommand("ada@example.com", tooShort));

        result.IsValid.ShouldBeFalse();
        var failure = result.Errors.ShouldHaveSingleItem();
        failure.PropertyName.ShouldBe(nameof(RegisterUserCommand.Password));
        failure.ErrorMessage.ShouldContain(
            RegisterUserCommandValidator.MinimumPasswordLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Validate_WellFormedEmailAndLongEnoughPassword_Succeeds()
    {
        var result = _validator.Validate(new RegisterUserCommand("ada@example.com", "correct horse battery staple"));

        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_PassphraseWithNoDigitsOrSymbols_Succeeds()
    {
        // Guards the policy decision, not just the code: there are deliberately no character-class rules, so.
        var result = _validator.Validate(new RegisterUserCommand("ada@example.com", "sixteen lower case"));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_PasswordEqualToEmail_FailsNamingThePasswordField()
    {
        const string Email = "a.very.long.address@example.com";

        var result = _validator.Validate(new RegisterUserCommand(Email, Email));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == nameof(RegisterUserCommand.Password));
    }

    [Fact]
    public void Validate_EmptyPassword_ReportsOneMessageNotTwo()
    {
        // Cascade.Stop: "required" and "too short" are the same complaint to a user staring at an empty box.
        var result = _validator.Validate(new RegisterUserCommand("ada@example.com", ""));

        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(RegisterUserCommand.Password));
    }
}
