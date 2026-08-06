using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Application.Names;
using StockPortfolio.Modules.MarketData.Application.Prices;
using StockPortfolio.Modules.MarketData.Application.Tickers.Queries.SearchTickers;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.MarketData.Infrastructure.Names;
using StockPortfolio.Modules.MarketData.Infrastructure.Polling;
using StockPortfolio.Modules.MarketData.Infrastructure.Prices;
using StockPortfolio.Modules.MarketData.Infrastructure.Quotes;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.MarketData.Infrastructure;

/// <summary>The MarketData module's entire public surface to the host.</summary>
public static class MarketDataModule
{
    // A default .NET user agent is a common WAF trigger; Finnhub's own client sends finnhub/python.
    private const string UserAgent = "StockPortfolio/1.0";

    /// <summary>Registers MarketData: a provider, the Redis stores, the contracts and the poller.</summary>
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

        services.AddSingleton<ILastKnownPriceStore, RedisLastKnownPriceStore>();
        services.AddSingleton<ICompanyNameStore, RedisCompanyNameStore>();
        services.AddSingleton<IPriceWindowStore, RedisPriceWindowStore>();
        services.AddScoped<IQuoteReader, QuoteReader>();
        services.AddScoped<IPriceWindowReader, PriceWindowReader>();
        services.AddScoped<ISymbolValidator, SymbolValidator>();
        services.AddScoped<ICompanyNameReader, CompanyNameReader>();

        // The module's first CQRS handler. Program.cs calls DecorateHandlers() after this, so it is
        // wrapped in the logging decorator like every other one.
        services.AddScoped<
            IQueryHandler<SearchTickersQuery, IReadOnlyList<SearchTickersResult>>,
            SearchTickersQueryHandler>();

        AddPolling(services, config);

        return services;
    }

    /// <summary>The poller, its two locks, and the observer the host is expected to replace.</summary>
    private static void AddPolling(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton(PollingOptions.FromConfiguration(config));
        services.AddSingleton<IPollLease, RedisPollLease>();

        // TryAdd, so a host that registers its adapter BEFORE this call still keeps it. A host registering
        // after wins on last-one-wins instead; either order leaves exactly one observer resolved.
        services.TryAddSingleton<IPriceSampleObserver, NoOpPriceSampleObserver>();

        // No default IPollTargetSource on purpose: "nobody told me what to poll" must be a visible failure
        // in the log, not an empty list that reads the same as "nobody has an alert".
        services.AddHostedService<QuotePoller>();
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
        // setting a generator silently overwrites Retry-After handling. With no client-side rate limiter,
        // this header is the provider's only way to tell this client to slow down.
    }
}
