namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RevokeSession;

/// <summary>
/// End a session — log out.
/// </summary>
/// <param name="RefreshToken">The opaque token string identifying the session to close.</param>
/// <remarks>
/// Named for the same CS0542 reason as <see cref="RefreshSession.RefreshSessionCommand"/>.
/// </remarks>
public sealed record RevokeSessionCommand(string RefreshToken);
