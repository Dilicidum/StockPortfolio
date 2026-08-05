namespace StockPortfolio.Modules.MarketData.Api.Requests;

/// <summary>The body of POST /api/dev/nudge: shift one generated price by a percentage, for a while.</summary>
public sealed record NudgeRequest(string Ticker, decimal Percent, int TtlSeconds);
