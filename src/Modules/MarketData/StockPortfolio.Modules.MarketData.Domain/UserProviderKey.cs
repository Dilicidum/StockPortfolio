namespace StockPortfolio.Modules.MarketData.Domain;

/// <summary>A user's own Finnhub API key, held encrypted so MarketData can fetch on their behalf.</summary>
public sealed class UserProviderKey
{
    private UserProviderKey(
        Guid userId, string ciphertext, string lastFour, DateTimeOffset savedAt, DateTimeOffset? lastRejectedAt)
    {
        UserId = userId;
        Ciphertext = ciphertext;
        LastFour = lastFour;
        SavedAt = savedAt;
        LastRejectedAt = lastRejectedAt;
    }

    public Guid UserId { get; private set; }

    // The user's provider key, already protected. Never leaves the server.
    public string Ciphertext { get; private set; }

    public string LastFour { get; private set; }

    public DateTimeOffset SavedAt { get; private set; }

    // Set when the provider refused this key on a real fetch, so the screen can say so.
    public DateTimeOffset? LastRejectedAt { get; private set; }

    public static UserProviderKey Create(Guid userId, string ciphertext, string lastFour, TimeProvider clock)
        => new(userId, ciphertext, lastFour, clock.GetUtcNow(), lastRejectedAt: null);

    public void Replace(string ciphertext, string lastFour, TimeProvider clock)
    {
        Ciphertext = ciphertext;
        LastFour = lastFour;
        SavedAt = clock.GetUtcNow();
        LastRejectedAt = null;
    }

    public void MarkRejected(TimeProvider clock) => LastRejectedAt = clock.GetUtcNow();
}
