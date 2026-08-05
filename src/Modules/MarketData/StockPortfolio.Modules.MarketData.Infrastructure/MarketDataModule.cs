using System.Threading.RateLimiting;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Application.Prices;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.MarketData.Infrastructure.Prices;
using StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

namespace StockPortfolio.Modules.MarketData.Infrastructure;

/// <summary>The MarketData module's entire public surface to the host.</summary>
public static class MarketDataModule
{
    // A default .NET user agent is a common WAF trigger; Finnhub's own client sends finnhub/python.
    private const string UserAgent = "StockPortfolio/1.0";

    /// <summary>Registers MarketData: a provider, the token budget, the fallback store and the two contracts.</summary>
    public static IServiceCollection AddMarketDataModule(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        // No eager validation of anything: a missing Finnhub key is a supported state and a throw here
        // would take down `docker compose up`, which is the P0 gate.
        var options = FinnhubOptions.FromConfiguration(config);

        services.AddSingleton(options);

        if (options.HasApiKey)
        {
            services.AddHttpClient<IQuoteProvider, FinnhubQuoteProvider>(client =>
                {
                    // No HttpClient.Timeout: the standard handler sets it to InfiniteTimeSpan and owns timeouts.
                    client.BaseAddress = options.BaseUrl;
                    client.DefaultRequestHeaders.Add("X-Finnhub-Token", options.ApiKey);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                })
                .AddStandardResilienceHandler(ConfigureResilience);
        }
        else
        {
            services.AddSingleton(FakeQuoteOptions.FromConfiguration(config));

            // One owner, no cast. Registering IQuoteNudge as a cast off IQuoteProvider breaks the moment a
            // test swaps the provider out: RemoveAll<IQuoteProvider>() leaves the lambda casting the replacement.
            services.AddSingleton<FakeQuoteProvider>();
            services.AddSingleton<IQuoteProvider>(sp => sp.GetRequiredService<FakeQuoteProvider>());
            services.AddSingleton<IQuoteNudge>(sp => sp.GetRequiredService<FakeQuoteProvider>());
        }

        // One bucket for the process: the typed client is transient, so a limiter held as a field on the
        // provider would be a fresh bucket per resolution, enforcing nothing across concurrent requests.
        services.AddSingleton<RateLimiter>(_ => BuildTokenBucket());

        services.AddSingleton<ILastKnownPriceStore, RedisLastKnownPriceStore>();
        services.AddScoped<IQuoteReader, QuoteReader>();
        services.AddScoped<ISymbolValidator, SymbolValidator>();

        return services;
    }

    private static void ConfigureResilience(HttpStandardResilienceOptions o)
    {
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
        o.Retry.MaxRetryAttempts = 2;

        // The shipped 100 can never trip on a twenty-ticker dashboard, so the breaker would be decoration.
        o.CircuitBreaker.MinimumThroughput = 10;

        // The validator runs under AddOptionsWithValidateOnStart and is startup-fatal: AttemptTimeout must
        // stay under TotalRequestTimeout, and SamplingDuration at or above twice AttemptTimeout.
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);

        // NEVER assign o.Retry.DelayGenerator - ShouldRetryAfterHeader's setter *is* that assignment, and
        // setting a generator silently overwrites Retry-After handling. MaxDelay is ignored for it too.
    }

    private static TokenBucketRateLimiter BuildTokenBucket() =>
        new(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 25,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            AutoReplenishment = true,
            QueueLimit = 256,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
}
