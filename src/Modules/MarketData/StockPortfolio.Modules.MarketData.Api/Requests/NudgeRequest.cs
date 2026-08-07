namespace StockPortfolio.Modules.MarketData.Api.Requests;

public sealed record NudgeRequest(string Ticker, decimal Percent, int TtlSeconds);
