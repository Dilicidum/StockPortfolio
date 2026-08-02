using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Infrastructure.Security;

/// <summary>Issues signed JWT access tokens and opaque refresh tokens.</summary>
internal sealed class JwtTokenIssuer : ITokenIssuer
{
    /// <summary>32 bytes = 256 bits of entropy, which is what licenses the plain SHA-256 below.</summary>
    internal const int RefreshTokenBytes = 32;

    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtTokenIssuer(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(options.GetSigningKeyBytes()),
            SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc/>
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
    public string NewRefreshToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RefreshTokenBytes));

    /// <inheritdoc/>
    public byte[] HashRefreshToken(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);

        return SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
    }
}
