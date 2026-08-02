using Shouldly;
using StockPortfolio.Modules.Identity.Api;
using StockPortfolio.Modules.Identity.Api.Validators;
using StockPortfolio.Modules.Identity.Api.Requests;

namespace StockPortfolio.Modules.Identity.UnitTests;

/// <summary>Login validates presence and nothing else.</summary>
public sealed class LoginUserRequestValidatorTests
{
    private readonly LoginUserRequestValidator _validator = new();

    [Fact]
    public void Validate_EmptyEmail_FailsNamingTheEmailField()
    {
        var result = _validator.Validate(new LoginUserRequest("", "correct horse battery staple"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(LoginUserRequest.Email));
    }

    [Fact]
    public void Validate_EmptyPassword_FailsNamingThePasswordField()
    {
        var result = _validator.Validate(new LoginUserRequest("ada@example.com", ""));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(LoginUserRequest.Password));
    }

    [Fact]
    public void Validate_BothFieldsEmpty_FailsNamingBoth()
    {
        var result = _validator.Validate(new LoginUserRequest("", ""));

        result.Errors.Select(failure => failure.PropertyName)
            .ShouldBe([nameof(LoginUserRequest.Email), nameof(LoginUserRequest.Password)], ignoreOrder: true);
    }

    [Fact]
    public void Validate_ShortPassword_Succeeds()
    {
        // Deliberate: the registration policy is not applied at sign-in.
        var result = _validator.Validate(new LoginUserRequest("ada@example.com", "short"));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_MalformedEmail_Succeeds()
    {
        // Also deliberate.
        var result = _validator.Validate(new LoginUserRequest("not-an-email", "correct horse battery staple"));

        result.IsValid.ShouldBeTrue();
    }
}
