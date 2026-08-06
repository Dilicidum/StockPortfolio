namespace StockPortfolio.Modules.MarketData.Application;

/// <summary>Whether a signed-in user may bring their own provider key, read once at startup.</summary>
public sealed record ByokOptions(bool Enabled)
{
    /// <summary>A switched-off feature is the exception, so the default ships turned on.</summary>
    public const bool DefaultEnabled = true;
}
