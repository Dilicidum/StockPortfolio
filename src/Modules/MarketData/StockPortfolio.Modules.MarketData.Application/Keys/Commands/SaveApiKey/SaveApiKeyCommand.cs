namespace StockPortfolio.Modules.MarketData.Application.Keys.Commands.SaveApiKey;

/// <summary>Saves or replaces the caller's own provider key. The user comes from the bearer token.</summary>
public sealed record SaveApiKeyCommand(Guid UserId, string ApiKey);
