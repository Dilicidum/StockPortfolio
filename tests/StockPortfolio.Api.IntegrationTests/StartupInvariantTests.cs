using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Host.Extensions;
using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

public sealed class StartupInvariantTests
{
    private const string RetentionKey = "MarketData:Polling:RetentionMinutes";
    private const string MaxWindowKey = "Alerts:MaxWindowMinutes";

    [Fact]
    public void ValidateAlertWindowFitsRetention_RetentionOutlastsTheWindow_DoesNotThrow()
    {
        var services = new ServiceCollection();

        Should.NotThrow(() => services.ValidateAlertWindowFitsRetention(Configuration("75", "60")));
    }

    // Equal is refused: the last sample the evaluator needs is one trimming is free to have dropped, so < rather than <= would let it through.
    [Fact]
    public void ValidateAlertWindowFitsRetention_RetentionEqualsTheWindow_Throws()
    {
        var services = new ServiceCollection();

        Should.Throw<InvalidOperationException>(
            () => services.ValidateAlertWindowFitsRetention(Configuration("60", "60")));
    }

    [Fact]
    public void ValidateAlertWindowFitsRetention_RetentionShorterThanTheWindow_Throws()
    {
        var services = new ServiceCollection();

        var thrown = Should.Throw<InvalidOperationException>(
            () => services.ValidateAlertWindowFitsRetention(Configuration("30", "60")));

        thrown.Message.ShouldContain(RetentionKey);
        thrown.Message.ShouldContain(MaxWindowKey);
        thrown.Message.ShouldContain("30");
        thrown.Message.ShouldContain("60");
    }

    // The two fallback constants are hand-copied from two modules' options, so nothing but this test says they still agree.
    [Fact]
    public void ValidateAlertWindowFitsRetention_NeitherKeyIsConfigured_DoesNotThrow()
    {
        var services = new ServiceCollection();

        Should.NotThrow(() => services.ValidateAlertWindowFitsRetention(Configuration(null, null)));
    }

    // A zero is a missing value, not a value: read literally, the host refuses to start on a placeholder every other reader ignores.
    [Fact]
    public void ValidateAlertWindowFitsRetention_RetentionIsZero_FallsBackToTheDefaultAndDoesNotThrow()
    {
        var services = new ServiceCollection();

        Should.NotThrow(() => services.ValidateAlertWindowFitsRetention(Configuration("0", null)));
    }

    // The numbers the repository actually ships: raising the window without raising retention is the mistake this check exists for.
    [Fact]
    public void ValidateAlertWindowFitsRetention_TheShippedAppSettings_DoesNotThrow()
    {
        var path = Path.Combine(RepositoryPaths.Root, "src", "Host", "appsettings.json");

        File.Exists(path).ShouldBeTrue(
            $"'{path}' is what the host loads at startup. If this test cannot find it, the assertion "
                + "below reads an empty configuration and passes on the fallback constants instead.");

        var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();

        // A missing key here would silently fall back, so prove both are present before comparing them.
        configuration[RetentionKey].ShouldNotBeNullOrEmpty();
        configuration[MaxWindowKey].ShouldNotBeNullOrEmpty();

        Should.NotThrow(() => new ServiceCollection().ValidateAlertWindowFitsRetention(configuration));
    }

    private static IConfiguration Configuration(string? retentionMinutes, string? maxWindowMinutes)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (retentionMinutes is not null)
        {
            settings[RetentionKey] = retentionMinutes;
        }

        if (maxWindowMinutes is not null)
        {
            settings[MaxWindowKey] = maxWindowMinutes;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }
}
