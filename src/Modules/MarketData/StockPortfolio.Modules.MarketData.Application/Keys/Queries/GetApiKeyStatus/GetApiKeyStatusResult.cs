namespace StockPortfolio.Modules.MarketData.Application.Keys.Queries.GetApiKeyStatus;

public sealed record GetApiKeyStatusResult(bool Configured, string? LastFour, bool Rejected);
