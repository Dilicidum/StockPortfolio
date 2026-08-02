using Shouldly;
using StockPortfolio.Modules.Identity.Api;
using StockPortfolio.Modules.Identity.Api.Validators;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.LoginUser;

namespace StockPortfolio.Modules.Identity.UnitTests;

/// <summary>Login validates presence and nothing else.</summary>
public sealed class LoginUserCommandValidatorTests
{
    private readonly LoginUserCommandValidator _validator = new();

    [Fact]
    public void Validate_EmptyEmail_FailsNamingTheEmailField()
    {
        var result = _validator.Validate(new LoginUserCommand("", "correct horse battery staple"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(LoginUserCommand.Email));
    }

    [Fact]
    public void Validate_EmptyPassword_FailsNamingThePasswordField()
    {
        var result = _validator.Validate(new LoginUserCommand("ada@example.com", ""));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(LoginUserCommand.Password));
    }

    [Fact]
    public void Validate_BothFieldsEmpty_FailsNamingBoth()
    {
        var result = _validator.Validate(new LoginUserCommand("", ""));

        result.Errors.Select(failure => failure.PropertyName)
            .ShouldBe([nameof(LoginUserCommand.Email), nameof(LoginUserCommand.Password)], ignoreOrder: true);
    }

    [Fact]
    public void Validate_ShortPassword_Succeeds()
    {
        // Deliberate: the registration policy is not applied at sign-in.
        var result = _validator.Validate(new LoginUserCommand("ada@example.com", "short"));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_MalformedEmail_Succeeds()
    {
        // Also deliberate.
        var result = _validator.Validate(new LoginUserCommand("not-an-email", "correct horse battery staple"));

        result.IsValid.ShouldBeTrue();
    }
}
