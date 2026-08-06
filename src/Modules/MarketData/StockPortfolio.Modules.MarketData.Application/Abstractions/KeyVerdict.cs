namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

/// <summary>What the provider said about a candidate key. Unknown is its own case, never folded into Rejected.</summary>
public enum KeyVerdict
{
    Accepted,
    Rejected,
    Unknown,
}
