namespace StockPortfolio.Modules.MarketData.Api.Requests;

/// <summary>The body of POST /api/settings/api-key.</summary>
public sealed record SaveApiKeyRequest(string ApiKey);
