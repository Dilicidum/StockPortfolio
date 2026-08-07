namespace StockPortfolio.Modules.Identity.Domain;

public sealed class UserPreferences
{
    private UserPreferences(Guid userId, ThemeChoice theme, LanguageChoice language)
    {
        UserId = userId;
        Theme = theme;
        Language = language;
    }

    public Guid UserId { get; private set; }

    public ThemeChoice Theme { get; private set; }

    public LanguageChoice Language { get; private set; }

    public static UserPreferences CreateDefault(Guid userId) =>
        new(userId, ThemeChoice.System, LanguageChoice.English);

    public void ChangeAppearance(ThemeChoice theme, LanguageChoice language)
    {
        Theme = theme;
        Language = language;
    }
}
