using System.Globalization;
using System.Text;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace StockPortfolio.Api.Extensions;

/// <summary>
/// Bearer-token authentication for the whole host.
/// </summary>
/// <remarks>
/// <para>
/// The <c>Jwt</c> section is read here a second time — <c>IdentityModule.AddIdentityModule</c> already
/// reads it to build its internal <c>JwtOptions</c> for the <i>issuing</i> side. That type is
/// <see langword="internal"/> to <c>Identity.Infrastructure</c> and the host cannot name it, so the
/// alternative would be widening the module's public surface purely to hand the host three strings it
/// can read from configuration itself. Both sides read identical keys and apply identical defaults;
/// a mismatch would fail every request loudly at the first login, not silently.
/// </para>
/// <para>
/// This is also why <c>AddOptions&lt;JwtOptions&gt;().ValidateOnStart()</c> does not appear in
/// <c>Program.cs</c>: binding a type the host cannot name is not expressible. Validation happens
/// eagerly here and inside the module instead, which fails earlier and with a plainer stack.
/// </para>
/// </remarks>
public static class AuthenticationExtensions
{
    /// <summary>The configuration section carrying the signing settings: <c>Jwt__SigningKey</c> and friends.</summary>
    public const string JwtSectionName = "Jwt";

    /// <summary>HMAC-SHA256 keys shorter than the 256-bit output are rejected outright by <see cref="SymmetricSecurityKey"/>.</summary>
    private const int MinimumSigningKeyBytes = 32;

    /// <summary>Mirrors the module's own default so issuer and validator cannot drift when the key is unset.</summary>
    private const string DefaultIssuer = "StockPortfolio";

    /// <inheritdoc cref="DefaultIssuer"/>
    private const string DefaultAudience = "StockPortfolio";

    /// <summary>
    /// Registers JWT bearer authentication against the <c>Jwt</c> configuration section.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">Configuration carrying <c>Jwt:SigningKey</c>, <c>Jwt:Issuer</c> and <c>Jwt:Audience</c>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// The signing key is missing or shorter than 32 UTF-8 bytes. Checked during registration so a
    /// misconfigured deployment fails at startup rather than 401-ing every request in production.
    /// </exception>
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
                // THE line. JwtBearerOptions.MapInboundClaims defaults to TRUE even though
                // JsonWebTokenHandler.MapInboundClaims defaults to false - the options object overrides the
                // handler. Left at the default, `sub` arrives renamed to the long
                // http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier URI, and
                // IdentityEndpoints' FindFirstValue("sub") returns null forever: a 401 on every
                // authenticated request with nothing in the logs to explain it.
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

                    // Only the algorithm we issue with. Without this an attacker-supplied `alg` header is
                    // whatever the library is willing to accept, which is a larger set than one.
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

                    // The default is five minutes, which quietly extends every access token's lifetime by
                    // that much. Server and client clocks are both NTP-synced here; 30 seconds is slack,
                    // not policy.
                    ClockSkew = TimeSpan.FromSeconds(30),

                    // Belt and braces with MapInboundClaims = false: these decide what
                    // ClaimsPrincipal.Identity.Name and IsInRole read, which is not covered by the mapping
                    // switch. Naming them means no code path falls back to a Microsoft schema URI.
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = "role",
                };
            });

        return services;
    }
}
