namespace StockPortfolio.Modules.Identity.Application.Authentication.Queries.GetCurrentUser;

/// <summary>Everything the application will tell you about yourself.</summary>
public sealed record UserSummary(Guid Id, string Email);
