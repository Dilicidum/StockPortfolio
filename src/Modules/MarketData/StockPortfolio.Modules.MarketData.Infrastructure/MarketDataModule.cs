using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

using StockPortfolio.Modules.MarketData.Application;
using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Application.Names;
using StockPortfolio.Modules.MarketData.Application.Prices;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.MarketData.Infrastructure.Health;
using StockPortfolio.Modules.MarketData.Infrastructure.Names;
using StockPortfolio.Modules.MarketData.Infrastructure.Persistence;
using StockPortfolio.Modules.MarketData.Infrastructure.Polling;
using StockPortfolio.Modules.MarketData.Infrastructure.Prices;
using StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

namespace StockPortfolio.Modules.MarketData.Infrastructure;

public static class MarketDataModule
{
    private const string UserAgent = "StockPortfolio/1.0";

    public const string ConnectionStringName = "MarketData";

    public const string FeedCheckName = "marketdata-feed";

    private const string ByokEnabledKey = "MarketData:Byok:Enabled";

    public static IServiceCollection AddMarketDataPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. Set "
                + $"ConnectionStrings:{ConnectionStringName} (or ConnectionStrings__{ConnectionStringName}). "
                + "Passing a null connection string to UseNpgsql throws later, from a stack that names "
                + "neither the key nor the file.");
        }

        services.AddDbContext<MarketDataDbContext>(options => options.UseNpgsql(
            connectionString,
            npg =>
            {
                npg.MigrationsHistoryTable(
                    MarketDataDbContext.MigrationsHistoryTableName,
                    MarketDataDbContext.SchemaName);

                npg.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(2), errorCodesToAdd: null);
            }));

        services.AddHealthChecks().AddDbContextCheck<MarketDataDbContext>("postgres-marketdata", tags: ["ready", "detail"]);

        services.AddSingleton<IKeyRingStore, KeyRingStore>();

        return services;
    }

    public static IServiceCollection AddMarketDataModule(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        services.AddMarketDataPersistence(config);

        var options = FinnhubOptions.FromConfiguration(config);

        services.AddSingleton(options);

        services.AddSingleton<ProviderKeyRejection>();

        if (options.HasApiKey)
        {
            services.AddHttpClient<IQuoteProvider, FinnhubQuoteProvider>(client =>
                {
                    client.BaseAddress = options.BaseUrl;
                    client.DefaultRequestHeaders.Add("X-Finnhub-Token", options.ApiKey);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                })
                .AddStandardResilienceHandler(ConfigureResilience);

            services.AddHttpClient(FinnhubQuoteProvider.ByokClientName, client =>
                {
                    client.BaseAddress = options.BaseUrl;
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                })
                .AddStandardResilienceHandler(ConfigureResilience);
        }
        else
        {
            services.AddSingleton(FakeQuoteOptions.FromConfiguration(config));

            services.AddSingleton<FakeQuoteProvider>();
            services.AddSingleton<IQuoteProvider>(sp => sp.GetRequiredService<FakeQuoteProvider>());
            services.AddSingleton<IQuoteNudge>(sp => sp.GetRequiredService<FakeQuoteProvider>());
        }

        services.AddSingleton<ILastKnownPriceStore, RedisLastKnownPriceStore>();
        services.AddSingleton<ICompanyNameStore, RedisCompanyNameStore>();
        services.AddSingleton<IPriceWindowStore, RedisPriceWindowStore>();
        services.AddScoped<IQuoteReader, QuoteReader>();
        services.AddScoped<IPriceWindowReader, PriceWindowReader>();
        services.AddScoped<ISymbolValidator, SymbolValidator>();
        services.AddScoped<ICompanyNameReader, CompanyNameReader>();
        services.AddScoped<IFeedHealth, FeedHealthReader>();

        services.AddHealthChecks().AddCheck<FeedHealthCheck>(FeedCheckName, tags: ["detail"]);

        services.AddMarketDataHandlers();

        AddKeys(services, config);

        AddPolling(services, config);

        return services;
    }

    private static void AddKeys(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton(ReadByokOptions(config));

        services.AddScoped<IUserProviderKeyRepository, UserProviderKeyRepository>();
        services.AddScoped<IUserProviderKeyReader, UserProviderKeyReader>();
    }

    private static ByokOptions ReadByokOptions(IConfiguration config) =>
        new(bool.TryParse(config[ByokEnabledKey], out var enabled) ? enabled : ByokOptions.DefaultEnabled);

    private static void AddPolling(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton(PollingOptions.FromConfiguration(config));
        services.AddSingleton<IPollLease, RedisPollLease>();
        services.AddSingleton<IPollHeartbeatStore, RedisPollHeartbeatStore>();

        // TryAdd, so a host adapter registered before this call survives; one registered after wins on last-registration-wins.
        services.TryAddSingleton<IPriceSampleObserver, NoOpPriceSampleObserver>();

        services.AddHostedService<QuotePoller>();
    }

    private static void ConfigureResilience(HttpStandardResilienceOptions o)
    {
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
        o.Retry.MaxRetryAttempts = 2;

        o.CircuitBreaker.MinimumThroughput = 10;

        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    }
}
