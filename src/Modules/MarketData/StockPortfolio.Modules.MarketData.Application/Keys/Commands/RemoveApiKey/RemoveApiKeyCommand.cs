namespace StockPortfolio.Modules.MarketData.Application.Keys.Commands.RemoveApiKey;

/// <summary>Forgets the caller's own provider key. The user comes from the bearer token.</summary>
public sealed record RemoveApiKeyCommand(Guid UserId);
