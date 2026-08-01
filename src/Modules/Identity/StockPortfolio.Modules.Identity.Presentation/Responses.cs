namespace StockPortfolio.Modules.Identity.Presentation;

/// <summary>
/// A freshly issued token pair. Returned by register, login and refresh alike.
/// </summary>
/// <param name="AccessToken">
/// A short-lived, self-contained JWT. Send it as <c>Authorization: Bearer &lt;token&gt;</c>.
/// </param>
/// <param name="RefreshToken">
/// The long-lived, opaque half of the session, used once to obtain the next pair.
/// </param>
/// <param name="AccessExpiresAt">
/// When <paramref name="AccessToken"/> stops being accepted, as an absolute instant with offset.
/// The client refreshes on a 401 rather than on a timer, so this is advisory — it exists so a UI
/// can show session state without decoding the JWT.
/// </param>
/// <remarks>
/// This is the transport shape, deliberately separate from the application's <c>TokenPair</c>.
/// The two are identical today; keeping them distinct means an application-side change does not
/// silently rewrite a wire contract the SPA is already compiled against.
/// </remarks>
public sealed record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt);

/// <summary>
/// The identity behind the current access token.
/// </summary>
/// <param name="Id">The user's stable identifier.</param>
/// <param name="Email">The user's normalised email address.</param>
public sealed record UserResponse(Guid Id, string Email);
