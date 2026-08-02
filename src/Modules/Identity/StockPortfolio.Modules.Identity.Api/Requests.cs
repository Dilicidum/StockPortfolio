namespace StockPortfolio.Modules.Identity.Api;

/// <summary>
/// Credentials for creating a new account.
/// </summary>
/// <param name="Email">
/// The address the account is keyed by. Normalised to trimmed lowercase by the domain, so
/// <c>Ada@Example.com</c> and <c>ada@example.com</c> are the same account.
/// </param>
/// <param name="Password">
/// The chosen password. Held only long enough to hash it; never stored or logged in the clear.
/// </param>
public sealed record RegisterRequest(string Email, string Password);

/// <summary>
/// Credentials for signing in to an existing account.
/// </summary>
/// <param name="Email">The address the account was registered with. Case-insensitive.</param>
/// <param name="Password">The account password.</param>
public sealed record LoginRequest(string Email, string Password);

/// <summary>
/// A request to exchange a refresh token for a fresh token pair.
/// </summary>
/// <param name="RefreshToken">
/// The opaque refresh token from the most recent token pair. The server stores only its SHA-256
/// hash, so a leaked database gives an attacker nothing replayable.
/// </param>
public sealed record RefreshRequest(string RefreshToken);

/// <summary>
/// An optional body for sign-out, naming the refresh token to revoke.
/// </summary>
/// <param name="RefreshToken">
/// The refresh token to revoke, or <see langword="null"/>.
/// </param>
/// <remarks>
/// The whole body is optional, which is why this carries no validator and why
/// <c>RefreshToken</c> is nullable. The wire contract is "bearer in, 204 out": the SPA signs out
/// by dropping its local session and does not send a body. Sending one is strictly better — it
/// lets the server retire the long-lived half of the session immediately instead of waiting for
/// it to expire — so the endpoint accepts it when offered rather than forcing a second route.
/// </remarks>
public sealed record LogoutRequest(string? RefreshToken);
