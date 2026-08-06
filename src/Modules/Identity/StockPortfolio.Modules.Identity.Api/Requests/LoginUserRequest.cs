namespace StockPortfolio.Modules.Identity.Api.Requests;

/// <summary>The body of POST /api/auth/login.</summary>
public sealed record LoginUserRequest(string Email, string Password);
