namespace StockPortfolio.Modules.Identity.Application;

/// <summary>
/// What a successful register, login or refresh hands back.
/// </summary>
/// <param name="AccessToken">
/// The signed JWT. Short-lived, self-contained, and the only thing the API checks on a normal
/// request — Identity has no inbound runtime coupling precisely because this token carries
/// everything the other modules need.
/// </param>
/// <param name="RefreshToken">
/// The opaque 32-byte session token, base64url encoded. Sent exactly once; only its SHA-256 hash
/// is stored.
/// </param>
/// <param name="AccessExpiresAt">
/// When <paramref name="AccessToken"/> stops being accepted. Returned so the client can refresh
/// ahead of a 401 instead of discovering the expiry the hard way.
/// </param>
/// <remarks>
/// Serialises camelCase — <c>accessToken</c>, <c>refreshToken</c>, <c>accessExpiresAt</c>. The SPA
/// is already coded against exactly those names.
/// </remarks>
public sealed record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt);
