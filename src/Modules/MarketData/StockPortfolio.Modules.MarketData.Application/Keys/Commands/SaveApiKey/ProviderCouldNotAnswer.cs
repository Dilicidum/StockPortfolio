namespace StockPortfolio.Modules.MarketData.Application.Keys.Commands.SaveApiKey;

/// <summary>The provider could not be asked at all — a timeout, a 5xx, an open circuit. Not a verdict on the key.</summary>
public sealed record ProviderCouldNotAnswer;
