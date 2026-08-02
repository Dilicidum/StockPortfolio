using Shouldly;
using StockPortfolio.Modules.Identity.Api;
using StockPortfolio.Modules.Identity.Api.Validators;

namespace StockPortfolio.Modules.Identity.UnitTests;

/// <summary>
/// Login validates presence and nothing else. The tests that assert what it does <i>not</i> reject
/// matter as much as the ones that assert what it does.
/// </summary>
public sealed class LoginRequestValidatorTests
{
    private readonly LoginRequestValidator _validator = new();

    [Fact]
    public void Validate_EmptyEmail_FailsNamingTheEmailField()
    {
        var result = _validator.Validate(new LoginRequest("", "correct horse battery staple"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(LoginRequest.Email));
    }

    [Fact]
    public void Validate_EmptyPassword_FailsNamingThePasswordField()
    {
        var result = _validator.Validate(new LoginRequest("ada@example.com", ""));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(LoginRequest.Password));
    }

    [Fact]
    public void Validate_BothFieldsEmpty_FailsNamingBoth()
    {
        var result = _validator.Validate(new LoginRequest("", ""));

        result.Errors.Select(failure => failure.PropertyName)
            .ShouldBe([nameof(LoginRequest.Email), nameof(LoginRequest.Password)], ignoreOrder: true);
    }

    [Fact]
    public void Validate_ShortPassword_Succeeds()
    {
        // Deliberate: the registration policy is not applied at sign-in. Enforcing it here would
        // answer "does an account with a short password exist?" with a 400 instead of a 401, and
        // would lock out every account that predates a future policy change.
        var result = _validator.Validate(new LoginRequest("ada@example.com", "short"));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_MalformedEmail_Succeeds()
    {
        // Also deliberate. An unparseable address matches no account, and the handler already
        // answers that with the same undifferentiated 401 it gives a wrong password. Rejecting it
        // here would make the failure shape depend on the input, which is the leak we are avoiding.
        var result = _validator.Validate(new LoginRequest("not-an-email", "correct horse battery staple"));

        result.IsValid.ShouldBeTrue();
    }
}
