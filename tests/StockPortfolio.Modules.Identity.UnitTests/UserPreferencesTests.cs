using Shouldly;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.UnitTests;

public sealed class UserPreferencesTests
{
    [Fact]
    public void CreateDefault_ForAUser_IsSystemThemeAndEnglish()
    {
        var preferences = UserPreferences.CreateDefault(Guid.NewGuid());

        preferences.Theme.ShouldBe(ThemeChoice.System);
        preferences.Language.ShouldBe(LanguageChoice.English);
    }

    [Fact]
    public void ChangeAppearance_WithBothValues_ReplacesBoth()
    {
        var preferences = UserPreferences.CreateDefault(Guid.NewGuid());

        preferences.ChangeAppearance(ThemeChoice.Dark, LanguageChoice.Ukrainian);

        preferences.Theme.ShouldBe(ThemeChoice.Dark);
        preferences.Language.ShouldBe(LanguageChoice.Ukrainian);
    }
}
