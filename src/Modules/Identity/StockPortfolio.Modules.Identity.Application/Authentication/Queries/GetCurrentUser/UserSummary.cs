namespace StockPortfolio.Modules.Identity.Application.Authentication.Queries.GetCurrentUser;

/// <summary>
/// Everything the application will tell you about yourself.
/// </summary>
/// <param name="Id">The user id.</param>
/// <param name="Email">The sign-in address, in its stored lower-case form.</param>
/// <remarks>
/// Notably absent: the password hash, and the creation timestamp. A projection, not the entity —
/// the aggregate never crosses the HTTP boundary.
/// </remarks>
public sealed record UserSummary(Guid Id, string Email);
