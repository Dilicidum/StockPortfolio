namespace StockPortfolio.Modules.Identity.Application.Authentication.Queries.GetCurrentUser;

/// <summary>Everything the application will tell you about yourself.</summary>
public sealed record GetCurrentUserResult(Guid Id, string Email);
