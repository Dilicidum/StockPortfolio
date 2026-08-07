namespace StockPortfolio.Modules.MarketData.Application;

public sealed record ByokOptions(bool Enabled)
{
    public const bool DefaultEnabled = true;
}
