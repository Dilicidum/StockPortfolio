using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Application.Preferences.Commands.SaveAppearance;

// Maps ThemeChoice and LanguageChoice to and from their wire spellings. Shared by the read and the
// write side of appearance settings, both of which live in this feature area.
internal static class Wire
{
    public static string Theme(ThemeChoice theme) => theme switch
    {
        ThemeChoice.Light => "light",
        ThemeChoice.Dark => "dark",
        ThemeChoice.System => "system",
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null),
    };

    public static string Language(LanguageChoice language) => language switch
    {
        LanguageChoice.English => "en",
        LanguageChoice.Ukrainian => "uk",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
    };

    public static bool TryParseTheme(string wire, out ThemeChoice theme)
    {
        switch (wire)
        {
            case "light":
                theme = ThemeChoice.Light;
                return true;
            case "dark":
                theme = ThemeChoice.Dark;
                return true;
            case "system":
                theme = ThemeChoice.System;
                return true;
            default:
                theme = default;
                return false;
        }
    }

    public static bool TryParseLanguage(string wire, out LanguageChoice language)
    {
        switch (wire)
        {
            case "en":
                language = LanguageChoice.English;
                return true;
            case "uk":
                language = LanguageChoice.Ukrainian;
                return true;
            default:
                language = default;
                return false;
        }
    }
}
