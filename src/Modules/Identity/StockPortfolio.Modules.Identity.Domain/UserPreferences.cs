namespace StockPortfolio.Modules.Identity.Domain;

public sealed class UserPreferences
{
    // EF binds this by parameter name on every row it loads, so it assigns and does nothing else.
    private UserPreferences(Guid userId, ThemeChoice theme, LanguageChoice language)
    {
        UserId = userId;
        Theme = theme;
        Language = language;
    }

    // A uuid, matching AppUser.Id and every sibling module's owner column.
    public Guid UserId { get; private set; }

    public ThemeChoice Theme { get; private set; }

    public LanguageChoice Language { get; private set; }

    // The row a user gets the first time anything reads their preferences.
    public static UserPreferences CreateDefault(Guid userId) =>
        new(userId, ThemeChoice.System, LanguageChoice.English);

    public void ChangeAppearance(ThemeChoice theme, LanguageChoice language)
    {
        Theme = theme;
        Language = language;
    }
}
