using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Infrastructure.Persistence;
using StockPortfolio.Modules.Identity.Infrastructure.Security;

// The unit tests exercise Argon2PasswordHasher and PhcString directly. Both are internal, and making
// them public to test them would enlarge the module's surface to satisfy a test - exactly backwards.
[assembly: InternalsVisibleTo("StockPortfolio.Modules.Identity.UnitTests")]

namespace StockPortfolio.Modules.Identity.Infrastructure;

/// <summary>
/// The Identity module's entire public surface to the host. Everything else in this assembly is
/// <see langword="internal"/>.
/// </summary>
/// <remarks>
/// <c>AddDbContext&lt;IdentityDbContext&gt;()</c> with an internal context inside a public method
/// compiles: the inconsistent-accessibility rules (CS0051/CS0053) constrain signatures — parameter and
/// return types — not generic arguments used in a method body.
/// </remarks>
public static class IdentityModule
{
    /// <summary>The <c>ConnectionStrings</c> key this module reads. It connects as <c>identity_svc</c>.</summary>
    public const string ConnectionStringName = "Identity";

    /// <summary>
    /// Registers the Identity module: its <c>DbContext</c>, repositories, unit of work, password hasher,
    /// token issuer and every handler.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The <c>Identity</c> connection string or the <c>Jwt:SigningKey</c> setting is missing. Both are
    /// checked here, during registration, so a misconfigured deployment fails at startup rather than on
    /// the first request that happens to need them.
    /// </exception>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        var connectionString = config.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. Set "
                + $"ConnectionStrings:{ConnectionStringName} (or ConnectionStrings__{ConnectionStringName}). "
                + "Passing a null connection string to UseNpgsql throws later, from a stack that names "
                + "neither the key nor the file.");
        }

        services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(
            connectionString,
            npg => npg.MigrationsHistoryTable(
                IdentityDbContext.MigrationsHistoryTableName,
                IdentityDbContext.SchemaName)));

        // Validated eagerly - a bad signing key must not wait for the first login to surface.
        services.AddSingleton(JwtOptions.FromConfiguration(config));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Singletons: both are stateless and hold pre-computed state that is expensive to rebuild -
        // the hasher's dummy hash and the issuer's signing credentials.
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();

        services.AddIdentityHandlers();

        return services;
    }
}
