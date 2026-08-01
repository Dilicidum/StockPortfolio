namespace StockPortfolio.Modules.Identity.Application.Revoke;

/// <summary>
/// End a session — log out.
/// </summary>
/// <param name="RefreshToken">The opaque token string identifying the session to close.</param>
/// <remarks>
/// Named for the same CS0542 reason as <see cref="Refresh.RefreshSession"/>.
/// </remarks>
public sealed record RevokeSession(string RefreshToken);
