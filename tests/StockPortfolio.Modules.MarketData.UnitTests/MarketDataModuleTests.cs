using System.Net;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.MarketData.Domain;
using StockPortfolio.Modules.MarketData.Infrastructure;

namespace StockPortfolio.Tests;

public sealed class MarketDataModuleTests
{
    // AddMarketDataPersistence throws without one; AddInMemoryCollection is called twice so a test value still overrides it.
    private const string FallbackConnectionString =
        "Host=localhost;Database=marketdata-unit-tests;Username=none;Password=none";

    private static IConfiguration Config(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new[] { new KeyValuePair<string, string?>("ConnectionStrings:MarketData", FallbackConnectionString) })
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    private static ServiceProvider Build(IConfiguration config, Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddMarketDataModule(config);

        extra?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private static ServiceLifetime Lifetime<TService>(IServiceCollection services) =>
        services.Single(descriptor => descriptor.ServiceType == typeof(TService)).Lifetime;

    [Fact]
    public void Module_WithNoApiKey_BootsOntoTheFakeProviderRatherThanThrowing()
    {
        using var provider = Build(Config());

        provider.GetRequiredService<IQuoteProvider>().Name.ShouldBe("Fake");
        provider.GetRequiredService<IQuoteNudge>().ShouldBeSameAs(provider.GetRequiredService<IQuoteProvider>());
    }

    [Fact]
    public void Module_WithABlankApiKey_IsStillTheKeylessState()
    {
        using var provider = Build(Config(("Finnhub:ApiKey", "   ")));

        provider.GetRequiredService<IQuoteProvider>().Name.ShouldBe("Fake");
    }

    [Fact]
    public void Module_PriceWindow_RegistersTheStoreOnceAndTheReaderPerRequest()
    {
        // Read off the collection, not the provider: both types need IConnectionMultiplexer, which only the host registers.
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddMarketDataModule(Config());

        Lifetime<IPriceWindowStore>(services).ShouldBe(ServiceLifetime.Singleton);
        Lifetime<IPriceWindowReader>(services).ShouldBe(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Module_Polling_RegistersThePollerAndADefaultObserver()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddMarketDataModule(Config());

        services.Any(descriptor =>
                descriptor.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
                && descriptor.ImplementationType?.Name == "QuotePoller")
            .ShouldBeTrue();

        // IPollTargetSource is deliberately unregistered: a host that never said what to poll must fail loudly, not poll nothing.
        Lifetime<IPriceSampleObserver>(services).ShouldBe(ServiceLifetime.Singleton);
        services.Any(descriptor => descriptor.ServiceType == typeof(IPollTargetSource)).ShouldBeFalse();
    }

    [Fact]
    public void Module_FeedHealth_IsRegisteredOnBothProviderBranches()
    {
        foreach (var config in new[] { Config(), Config(("Finnhub:ApiKey", "a-real-looking-key")) })
        {
            var services = new ServiceCollection();

            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddMarketDataModule(config);

            Lifetime<IFeedHealth>(services).ShouldBe(ServiceLifetime.Scoped);
            Lifetime<IPollHeartbeatStore>(services).ShouldBe(ServiceLifetime.Singleton);

            services.Any(descriptor =>
                    string.Equals(descriptor.ServiceType.Name, "ProviderKeyRejection", StringComparison.Ordinal)
                    && descriptor.Lifetime == ServiceLifetime.Singleton)
                .ShouldBeTrue();
        }
    }

    [Fact]
    public void Module_HostRegistersAnObserverAfterwards_TheHostsWins()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddMarketDataModule(Config());
        services.AddScoped<IPriceSampleObserver, SpyObserver>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Last registration wins is what makes the host's adapter resolve; the module's TryAdd is not.
        scope.ServiceProvider.GetRequiredService<IPriceSampleObserver>().ShouldBeOfType<SpyObserver>();
    }

    [Fact]
    public async Task Finnhub401_IsNotRetriedByTheResiliencePipeline()
    {
        var handler = new CountingHandler(HttpStatusCode.Unauthorized);

        // Resolving the typed client runs HttpStandardResilienceOptionsCustomValidator, which is startup-fatal if the timeouts disagree.
        using var services = Build(
            Config(("Finnhub:ApiKey", "a-real-looking-key")),
            extra => extra
                .AddHttpClient<IQuoteProvider, StockPortfolio.Modules.MarketData.Infrastructure.Quotes.FinnhubQuoteProvider>()
                .ConfigurePrimaryHttpMessageHandler(() => handler));

        var quotes = await services.GetRequiredService<IQuoteProvider>().GetQuotesAsync(
            new HashSet<Ticker> { Ticker.Create("AAPL").AsT0 },
            apiKeyOverride: null,
            TestContext.Current.CancellationToken);

        quotes.ShouldBeEmpty();
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task GetQuotes_WhenTheProviderReturns429_RetriesRatherThanFailing()
    {
        var handler = new CountingHandler(HttpStatusCode.TooManyRequests, thenOk: true);

        using var services = Build(
            Config(("Finnhub:ApiKey", "a-real-looking-key")),
            extra => extra
                .AddHttpClient<IQuoteProvider, StockPortfolio.Modules.MarketData.Infrastructure.Quotes.FinnhubQuoteProvider>()
                .ConfigurePrimaryHttpMessageHandler(() => handler));

        await services.GetRequiredService<IQuoteProvider>().GetQuotesAsync(
            new HashSet<Ticker> { Ticker.Create("AAPL").AsT0 },
            apiKeyOverride: null,
            TestContext.Current.CancellationToken);

        handler.Calls.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void Module_WithAnApiKey_UsesFinnhubAndItsResilienceOptionsValidate()
    {
        using var services = Build(Config(("Finnhub:ApiKey", "a-real-looking-key")));

        services.GetRequiredService<IQuoteProvider>().Name.ShouldBe("Finnhub");
    }

    private sealed class SpyObserver : IPriceSampleObserver
    {
        public Task OnSampleStoredAsync(string ticker, CancellationToken ct) => Task.CompletedTask;
    }
}
