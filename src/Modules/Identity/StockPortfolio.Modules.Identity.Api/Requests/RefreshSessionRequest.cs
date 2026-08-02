namespace StockPortfolio.Modules.Identity.Api.Requests;

/// <summary>The body of POST /api/auth/refresh.</summary>
public sealed record RefreshSessionRequest(string RefreshToken);
