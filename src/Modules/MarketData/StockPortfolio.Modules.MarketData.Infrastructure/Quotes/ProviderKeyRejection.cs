namespace StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

/// <summary>Process-wide: the APPLICATION's own key was refused, which a user's own key never raises.</summary>
internal sealed class ProviderKeyRejection
{
    private int raised;

    public bool IsRejected => Volatile.Read(ref raised) == 1;

    public void Raise() => Volatile.Write(ref raised, 1);

    // A 403 can come from a proxy in front of the provider rather than from the provider, so this must be
    // clearable: without it one such response pins the feed unhealthy until the process restarts.
    public void Clear() => Volatile.Write(ref raised, 0);
}
