namespace StockPortfolio.Modules.MarketData.Application.Keys.Commands.SaveApiKey;

/// <summary>What a successful save reports. The key itself is never in here — see LastFour.</summary>
public sealed record SaveApiKeyResult(string LastFour);
