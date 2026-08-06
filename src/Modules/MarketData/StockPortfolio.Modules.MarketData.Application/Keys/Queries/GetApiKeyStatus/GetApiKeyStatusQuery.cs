namespace StockPortfolio.Modules.MarketData.Application.Keys.Queries.GetApiKeyStatus;

/// <summary>Asks whether the caller has a provider key on file. The user comes from the bearer token.</summary>
public sealed record GetApiKeyStatusQuery(Guid UserId);
