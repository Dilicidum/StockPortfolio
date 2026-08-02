using System.Globalization;
using System.Text;

using Microsoft.Extensions.Configuration;

namespace StockPortfolio.Modules.Identity.Infrastructure.Security;

/// <summary>The signing settings for access tokens, read from the Jwt configuration section.</summary>
internal sealed class JwtOptions
{
    internal const string SectionName = "Jwt";

    /// <summary>HMAC-SHA256 keys shorter than the 256-bit output are rejected outright by SymmetricSecurityKey.</summary>
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

    /// <summary>Reads and validates the Jwt section, throwing if the module cannot sign tokens.</summary>
    public static JwtOptions FromConfiguration(IConfiguration configuration)
    {
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
