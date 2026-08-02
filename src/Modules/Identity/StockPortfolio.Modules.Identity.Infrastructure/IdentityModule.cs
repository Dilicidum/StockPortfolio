using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Infrastructure.Persistence;
using StockPortfolio.Modules.Identity.Infrastructure.Security;

// The unit tests exercise Argon2PasswordHasher and PhcString directly.
[assembly: InternalsVisibleTo("StockPortfolio.Modules.Identity.UnitTests")]

namespace StockPortfolio.Modules.Identity.Infrastructure;

/// <summary>The Identity module's entire public surface to the host.</summary>
public static class IdentityModule
{
    /// <summary>The ConnectionStrings key this module reads.</summary>
    public const string ConnectionStringName = "Identity";

    /// <summary>Registers the Identity module: its DbContext, repositories, unit of work, password hasher, token.</summary>
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

        // Singletons: both are stateless and hold expensive pre-computed state.
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();

        services.AddIdentityHandlers();

        return services;
    }
}
