namespace StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

/// <summary>One-shot and process-wide: the APPLICATION's own key was refused, which a user's own key never raises.</summary>
internal sealed class ProviderKeyRejection
{
    private int raised;

    public bool IsRejected => Volatile.Read(ref raised) == 1;

    public void Raise() => Volatile.Write(ref raised, 1);
}
