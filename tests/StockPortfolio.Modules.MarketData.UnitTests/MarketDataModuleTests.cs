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
    private static IConfiguration Config(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
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
        // Read off the collection rather than the provider: both types need IConnectionMultiplexer, which
        // AddStockPortfolioRedis registers in the host and nothing in this module declares.
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddMarketDataModule(Config());

        Lifetime<IPriceWindowStore>(services).ShouldBe(ServiceLifetime.Singleton);
        Lifetime<IPriceWindowReader>(services).ShouldBe(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Module_TokenBucket_IsOneInstanceForTheProcess()
    {
        using var provider = Build(Config());

        provider.GetRequiredService<System.Threading.RateLimiting.RateLimiter>()
            .ShouldBeSameAs(provider.GetRequiredService<System.Threading.RateLimiting.RateLimiter>());
    }

    [Fact]
    public async Task Finnhub401_IsNotRetriedByTheResiliencePipeline()
    {
        var handler = new CountingHandler(HttpStatusCode.Unauthorized);

        // Resolving the typed client is also what runs HttpStandardResilienceOptionsCustomValidator, which
        // is registered with AddOptionsWithValidateOnStart and is startup-fatal if the timeouts disagree.
        using var services = Build(
            Config(("Finnhub:ApiKey", "a-real-looking-key")),
            extra => extra
                .AddHttpClient<IQuoteProvider, StockPortfolio.Modules.MarketData.Infrastructure.Quotes.FinnhubQuoteProvider>()
                .ConfigurePrimaryHttpMessageHandler(() => handler));

        var quotes = await services.GetRequiredService<IQuoteProvider>().GetQuotesAsync(
            new HashSet<Ticker> { Ticker.Create("AAPL").AsT0 },
            TestContext.Current.CancellationToken);

        quotes.ShouldBeEmpty();
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public void Module_WithAnApiKey_UsesFinnhubAndItsResilienceOptionsValidate()
    {
        using var services = Build(Config(("Finnhub:ApiKey", "a-real-looking-key")));

        services.GetRequiredService<IQuoteProvider>().Name.ShouldBe("Finnhub");
    }
}
