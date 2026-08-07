namespace StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

/// <summary>Process-wide: the APPLICATION's own key was refused, which a user's own key never raises.</summary>
internal sealed class ProviderKeyRejection
{
    private int raised;

    public bool IsRejected => Volatile.Read(ref raised) == 1;

    public void Raise() => Volatile.Write(ref raised, 1);

    // Clearable because a 403 can come from a proxy rather than the provider, which would otherwise pin the feed unhealthy until restart.
    public void Clear() => Volatile.Write(ref raised, 0);
}
