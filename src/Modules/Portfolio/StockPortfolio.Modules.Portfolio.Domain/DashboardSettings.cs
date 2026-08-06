namespace StockPortfolio.Modules.Portfolio.Domain;

public sealed class DashboardSettings
{
    // EF binds this by parameter name on every row it loads, so it assigns and does nothing else.
    private DashboardSettings(Guid userId, RefreshInterval refreshInterval)
    {
        UserId = userId;
        RefreshInterval = refreshInterval;
    }

    // A plain Guid: Portfolio does not own the Identity module's UserId, exactly as Holding does not.
    public Guid UserId { get; private set; }

    public RefreshInterval RefreshInterval { get; private set; }

    // The row a user gets the first time anything reads their dashboard settings.
    public static DashboardSettings CreateDefault(Guid userId) => new(userId, RefreshInterval.Default);

    public void ChangeInterval(RefreshInterval refreshInterval) => RefreshInterval = refreshInterval;
}
