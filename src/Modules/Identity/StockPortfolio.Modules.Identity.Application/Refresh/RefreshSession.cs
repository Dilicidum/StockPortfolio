namespace StockPortfolio.Modules.Identity.Application.Refresh;

/// <summary>
/// Trade a refresh token for a fresh token pair.
/// </summary>
/// <param name="RefreshToken">The opaque token string the client was handed.</param>
/// <remarks>
/// Named <c>RefreshSession</c>, not <c>RefreshToken</c>, for two reasons. A positional record
/// generates a member named after its parameter, and a member may not share the name of its
/// enclosing type — <c>record RefreshToken(string RefreshToken)</c> is CS0542. It would also
/// collide with the <see cref="Domain.RefreshToken"/> entity, forcing a <c>using</c> alias into
/// every file that touches both.
/// </remarks>
public sealed record RefreshSession(string RefreshToken);
