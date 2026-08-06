using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Options;

using StockPortfolio.Api.Adapters;
using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Api.Extensions;

/// <summary>Wires the framework's Data Protection key ring onto MarketData's Postgres-backed store.</summary>
internal static class DataProtectionExtensions
{
    /// <summary>Registers the protector, and points the framework's key ring at Postgres instead of the container filesystem.</summary>
    public static IServiceCollection AddStockPortfolioDataProtection(this IServiceCollection services)
    {
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        // Concrete, because the options callback below needs this exact type. IXmlRepository is never
        // registered as a service - the framework reads it off KeyManagementOptions, not from DI.
        services.AddSingleton<KeyRingXmlRepository>();

        services.AddDataProtection()
            .SetApplicationName("StockPortfolio")
            .Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
                new ConfigureOptions<KeyManagementOptions>(options =>
                    options.XmlRepository = sp.GetRequiredService<KeyRingXmlRepository>()));

        return services;
    }

    /// <summary>Fails at startup, not on the first saved key, if the host forgot to call AddStockPortfolioDataProtection.</summary>
    public static IServiceCollection ValidateSecretProtectorIsRegistered(this IServiceCollection services)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(ISecretProtector)))
        {
            throw new InvalidOperationException(
                "ISecretProtector has no registration. AddStockPortfolioDataProtection must run before this "
                + "check, or every saved provider key silently has nothing to encrypt it with.");
        }

        return services;
    }
}
