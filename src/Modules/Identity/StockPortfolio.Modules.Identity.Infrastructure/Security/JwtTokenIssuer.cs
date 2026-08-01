using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Issues signed JWT access tokens and opaque refresh tokens.
/// </summary>
/// <remarks>
/// <see cref="JsonWebTokenHandler"/>, not the legacy <c>JwtSecurityTokenHandler</c>: the modern handler is
/// several times faster, allocates far less, and — the reason it matters here — leaves claim types alone
/// instead of rewriting <c>sub</c> into a Microsoft schema URI.
/// </remarks>
internal sealed class JwtTokenIssuer : ITokenIssuer
{
    /// <summary>32 bytes = 256 bits of entropy, which is what licenses the plain SHA-256 below.</summary>
    internal const int RefreshTokenBytes = 32;

    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;
    private readonly JsonWebTokenHandler _handler = new();

    /// <remarks>
    /// The credentials are built once, in the constructor, because <see cref="JwtOptions"/> has already
    /// been validated by the time the container can construct this — so a bad signing key is a startup
    /// failure, never a first-login failure.
    /// </remarks>
    public JwtTokenIssuer(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(options.GetSigningKeyBytes()),
            SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>sub</c> carries the user id and <c>email</c> the address. The host sets
    /// <c>JwtBearerOptions.MapInboundClaims = false</c> so the API reads <c>"sub"</c> back verbatim; with
    /// the default of <see langword="true"/> it would have been renamed on the way in and
    /// <c>FindFirst("sub")</c> would silently return null.
    /// </remarks>
    public string IssueAccessToken(UserId userId, string email, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _signingCredentials,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [JwtRegisteredClaimNames.Sub] = userId.Value.ToString("D", CultureInfo.InvariantCulture),
                [JwtRegisteredClaimNames.Email] = email,
                [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString("D", CultureInfo.InvariantCulture),
            },
        };

        return _handler.CreateToken(descriptor);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// base64url, not base64: the token travels in JSON and, on the Pages deployment, through
    /// <c>sessionStorage</c> — <c>+</c> and <c>/</c> survive both but invite a mangled copy-paste, and
    /// padding is noise.
    /// </remarks>
    public string NewRefreshToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RefreshTokenBytes));

    /// <inheritdoc/>
    /// <remarks>
    /// SHA-256 with no work factor is the right choice here and not an oversight. A work factor exists to
    /// make guessing a low-entropy secret expensive; this secret is 256 random bits, so there is nothing
    /// to guess. Argon2 over it would buy no security and cost 19 MiB on every refresh.
    /// </remarks>
    public byte[] HashRefreshToken(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);

        return SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
    }
}
