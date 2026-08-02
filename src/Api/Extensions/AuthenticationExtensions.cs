using System.Globalization;
using System.Text;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace StockPortfolio.Api.Extensions;

/// <summary>Bearer-token authentication for the whole host.</summary>
public static class AuthenticationExtensions
{
    /// <summary>The configuration section carrying the signing settings: Jwt__SigningKey and friends.</summary>
    public const string JwtSectionName = "Jwt";

    /// <summary>HMAC-SHA256 keys shorter than the 256-bit output are rejected outright by SymmetricSecurityKey.</summary>
    private const int MinimumSigningKeyBytes = 32;

    /// <summary>Mirrors the module's own default so issuer and validator cannot drift when the key is unset.</summary>
    private const string DefaultIssuer = "StockPortfolio";

    private const string DefaultAudience = "StockPortfolio";

    /// <summary>Registers JWT bearer authentication against the Jwt configuration section.</summary>
    public static IServiceCollection AddStockPortfolioAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(JwtSectionName);
        var signingKey = section["SigningKey"];

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException(
                $"Configuration '{JwtSectionName}:SigningKey' is missing or empty. The API cannot validate "
                + $"access tokens without it. Set {JwtSectionName}__SigningKey in the environment, in user "
                + "secrets, or in appsettings.Development.json.");
        }

        var signingKeyBytes = Encoding.UTF8.GetBytes(signingKey);

        if (signingKeyBytes.Length < MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Configuration '{JwtSectionName}:SigningKey' is {signingKeyBytes.Length} UTF-8 bytes; "
                + $"HMAC-SHA256 requires at least {MinimumSigningKeyBytes}."));
        }

        var issuer = section["Issuer"] ?? DefaultIssuer;
        var audience = section["Audience"] ?? DefaultAudience;

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // THE line.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),

                    // Only the algorithm we issue with.
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

                    // The default is five minutes, which quietly extends every access token's lifetime by that much.
                    ClockSkew = TimeSpan.FromSeconds(30),

                    // Explicit with MapInboundClaims = false: these decide what Identity.Name and IsInRole read.
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = "role",
                };
            });

        return services;
    }
}
