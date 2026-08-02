namespace StockPortfolio.Modules.Identity.Api.Requests;

/// <summary>The body of POST /api/auth/logout, which is optional — logging out without one still returns 204.</summary>
public sealed record RevokeSessionRequest(string RefreshToken);
