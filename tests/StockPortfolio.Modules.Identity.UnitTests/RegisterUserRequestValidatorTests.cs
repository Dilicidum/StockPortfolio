using Shouldly;
using StockPortfolio.Modules.Identity.Api;
using StockPortfolio.Modules.Identity.Api.Validators;
using StockPortfolio.Modules.Identity.Api.Requests;

namespace StockPortfolio.Modules.Identity.UnitTests;

/// <summary>The shape layer of registration.</summary>
public sealed class RegisterUserRequestValidatorTests
{
    private static readonly string ValidPassword = new('a', RegisterUserRequestValidator.MinimumPasswordLength);

    private readonly RegisterUserRequestValidator _validator = new();

    [Fact]
    public void Validate_MalformedEmail_Fails()
    {
        var result = _validator.Validate(new RegisterUserRequest("not-an-email", ValidPassword));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_MalformedEmail_NamesTheEmailField()
    {
        var result = _validator.Validate(new RegisterUserRequest("not-an-email", ValidPassword));

        // The field name is the load-bearing part: ValidationFilter turns Errors into the `errors` dictionary.
        var failure = result.Errors.ShouldHaveSingleItem();
        failure.PropertyName.ShouldBe(nameof(RegisterUserRequest.Email));
        failure.ErrorMessage.ShouldContain("Email");
    }

    [Fact]
    public void Validate_PasswordShorterThanTheFloor_FailsNamingThePasswordField()
    {
        var tooShort = new string('a', RegisterUserRequestValidator.MinimumPasswordLength - 1);

        var result = _validator.Validate(new RegisterUserRequest("ada@example.com", tooShort));

        result.IsValid.ShouldBeFalse();
        var failure = result.Errors.ShouldHaveSingleItem();
        failure.PropertyName.ShouldBe(nameof(RegisterUserRequest.Password));
        failure.ErrorMessage.ShouldContain(
            RegisterUserRequestValidator.MinimumPasswordLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Validate_WellFormedEmailAndLongEnoughPassword_Succeeds()
    {
        var result = _validator.Validate(new RegisterUserRequest("ada@example.com", "correct horse battery staple"));

        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_PassphraseWithNoDigitsOrSymbols_Succeeds()
    {
        // Guards the policy decision, not just the code: there are deliberately no character-class rules, so.
        var result = _validator.Validate(new RegisterUserRequest("ada@example.com", "sixteen lower case"));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_PasswordEqualToEmail_FailsNamingThePasswordField()
    {
        const string Email = "a.very.long.address@example.com";

        var result = _validator.Validate(new RegisterUserRequest(Email, Email));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == nameof(RegisterUserRequest.Password));
    }

    [Fact]
    public void Validate_EmptyPassword_ReportsOneMessageNotTwo()
    {
        // Cascade.Stop: "required" and "too short" are the same complaint to a user staring at an empty box.
        var result = _validator.Validate(new RegisterUserRequest("ada@example.com", ""));

        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(RegisterUserRequest.Password));
    }
}
