using Shouldly;
using StockPortfolio.Modules.Identity.Api.Requests;
using StockPortfolio.Modules.Identity.Api.Validators;

namespace StockPortfolio.Modules.Identity.UnitTests;

/// <summary>The shape layer of saving appearance settings.</summary>
public sealed class SaveAppearanceRequestValidatorTests
{
    private readonly SaveAppearanceRequestValidator _validator = new();

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    [InlineData("system")]
    public void Validate_KnownTheme_Succeeds(string theme)
    {
        var result = _validator.Validate(new SaveAppearanceRequest(theme, "en"));

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("uk")]
    public void Validate_KnownLanguage_Succeeds(string language)
    {
        var result = _validator.Validate(new SaveAppearanceRequest("light", language));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_UnknownTheme_FailsWithTheThemeUnknownCode()
    {
        var result = _validator.Validate(new SaveAppearanceRequest("purple", "en"));

        var failure = result.Errors.ShouldHaveSingleItem();
        failure.PropertyName.ShouldBe(nameof(SaveAppearanceRequest.Theme));
        failure.ErrorCode.ShouldBe("theme.unknown");
    }

    [Fact]
    public void Validate_EmptyTheme_FailsWithTheThemeRequiredCode()
    {
        var result = _validator.Validate(new SaveAppearanceRequest("", "en"));

        var failure = result.Errors.ShouldHaveSingleItem();
        failure.PropertyName.ShouldBe(nameof(SaveAppearanceRequest.Theme));
        failure.ErrorCode.ShouldBe("theme.required");
    }

    [Fact]
    public void Validate_UnknownLanguage_FailsWithTheLanguageUnknownCode()
    {
        var result = _validator.Validate(new SaveAppearanceRequest("light", "fr"));

        var failure = result.Errors.ShouldHaveSingleItem();
        failure.PropertyName.ShouldBe(nameof(SaveAppearanceRequest.Language));
        failure.ErrorCode.ShouldBe("language.unknown");
    }

    [Fact]
    public void Validate_EmptyLanguage_FailsWithTheLanguageRequiredCode()
    {
        var result = _validator.Validate(new SaveAppearanceRequest("light", ""));

        var failure = result.Errors.ShouldHaveSingleItem();
        failure.PropertyName.ShouldBe(nameof(SaveAppearanceRequest.Language));
        failure.ErrorCode.ShouldBe("language.required");
    }

    [Fact]
    public void Validate_ThemeAndLanguageBothInvalid_NamesBothFields()
    {
        var result = _validator.Validate(new SaveAppearanceRequest("purple", "fr"));

        result.Errors.Select(e => e.PropertyName).ShouldBe(
            [nameof(SaveAppearanceRequest.Theme), nameof(SaveAppearanceRequest.Language)],
            ignoreOrder: true);
    }
}
