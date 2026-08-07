namespace StockPortfolio.Modules.MarketData.Application.Keys.Commands.SaveApiKey;

public sealed record SaveApiKeyCommand(Guid UserId, string ApiKey);
