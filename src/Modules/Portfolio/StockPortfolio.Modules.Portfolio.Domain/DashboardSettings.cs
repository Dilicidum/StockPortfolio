namespace StockPortfolio.Modules.Portfolio.Domain;

public sealed class DashboardSettings
{
    private DashboardSettings(Guid userId, RefreshInterval refreshInterval)
    {
        UserId = userId;
        RefreshInterval = refreshInterval;
    }

    public Guid UserId { get; private set; }

    public RefreshInterval RefreshInterval { get; private set; }

    public static DashboardSettings CreateDefault(Guid userId) => new(userId, RefreshInterval.Default);

    public void ChangeInterval(RefreshInterval refreshInterval) => RefreshInterval = refreshInterval;
}
