namespace StockPortfolio.Modules.Identity.Application.Authentication.Queries.GetCurrentUser;

/// <summary>Read the signed-in user.</summary>
public sealed record GetCurrentUserQuery(Guid UserId);
