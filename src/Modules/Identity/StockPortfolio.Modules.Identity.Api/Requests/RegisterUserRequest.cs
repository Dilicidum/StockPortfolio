namespace StockPortfolio.Modules.Identity.Api.Requests;

/// <summary>The body of POST /api/auth/register.</summary>
public sealed record RegisterUserRequest(string Email, string Password);
