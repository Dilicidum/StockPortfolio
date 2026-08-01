using System.Globalization;
using System.Text;

using Microsoft.Extensions.Configuration;

namespace StockPortfolio.Modules.Identity.Infrastructure.Security;

/// <summary>
/// The signing settings for access tokens, read from the <c>Jwt</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// Bound eagerly in <c>IdentityModule.AddIdentityModule</c> rather than through
/// <c>AddOptions&lt;T&gt;().ValidateOnStart()</c>. Two reasons: this type is internal, so the host cannot
/// name it in a generic argument; and the eager path fails during service registration, which is earlier
/// and produces a plainer stack trace than an options validation failure.
/// </para>
/// <para>
/// The failure mode this prevents is the expensive one: a missing or short signing key that surfaces at
/// the first login attempt, in production, as a 500 from deep inside the token handler.
/// </para>
/// </remarks>
internal sealed class JwtOptions
{
    internal const string SectionName = "Jwt";

    /// <summary>HMAC-SHA256 keys shorter than the 256-bit output are rejected outright by <c>SymmetricSecurityKey</c>.</summary>
    internal const int MinimumSigningKeyBytes = 32;

    private const string DefaultIssuer = "StockPortfolio";
    private const string DefaultAudience = "StockPortfolio";

    private readonly byte[] _signingKeyBytes;

    private JwtOptions(string issuer, string audience, byte[] signingKeyBytes)
    {
        Issuer = issuer;
        Audience = audience;
        _signingKeyBytes = signingKeyBytes;
    }

    public string Issuer { get; }

    public string Audience { get; }

    /// <summary>A copy, so a caller cannot zero or mutate the key held by the singleton.</summary>
    public byte[] GetSigningKeyBytes() => (byte[])_signingKeyBytes.Clone();

    /// <summary>Reads and validates the <c>Jwt</c> section, throwing if the module cannot sign tokens.</summary>
    /// <exception cref="InvalidOperationException">The signing key is absent or shorter than <see cref="MinimumSigningKeyBytes"/> bytes.</exception>
    public static JwtOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        var signingKey = section["SigningKey"];

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:SigningKey' is missing or empty. The Identity module cannot "
                + "sign access tokens without it. Set it in appsettings, user secrets, or the "
                + $"{SectionName}__SigningKey environment variable.");
        }

        var signingKeyBytes = Encoding.UTF8.GetBytes(signingKey);

        if (signingKeyBytes.Length < MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Configuration '{SectionName}:SigningKey' is {signingKeyBytes.Length} UTF-8 bytes; "
                + $"HMAC-SHA256 requires at least {MinimumSigningKeyBytes}."));
        }

        return new JwtOptions(
            section["Issuer"] ?? DefaultIssuer,
            section["Audience"] ?? DefaultAudience,
            signingKeyBytes);
    }
}
