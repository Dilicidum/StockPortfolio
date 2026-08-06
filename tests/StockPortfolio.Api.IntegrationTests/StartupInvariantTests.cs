using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Api.Extensions;
using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>The startup check that refuses to run when a user could ask for a window longer than the
/// price history the poller keeps. No host and no containers: it reads configuration and throws.</summary>
public sealed class StartupInvariantTests
{
    private const string RetentionKey = "MarketData:Polling:RetentionMinutes";
    private const string MaxWindowKey = "Alerts:MaxWindowMinutes";

    /// <summary>The shipped pair, and the case a flipped comparison would break: it must start.</summary>
    [Fact]
    public void ValidateAlertWindowFitsRetention_RetentionOutlastsTheWindow_DoesNotThrow()
    {
        var services = new ServiceCollection();

        Should.NotThrow(() => services.ValidateAlertWindowFitsRetention(Configuration("75", "60")));
    }

    /// <summary>The boundary. Equal is refused, because the last sample the evaluator needs is the one
    /// trimming is free to have already dropped — so &lt; rather than &lt;= would let it through.</summary>
    [Fact]
    public void ValidateAlertWindowFitsRetention_RetentionEqualsTheWindow_Throws()
    {
        var services = new ServiceCollection();

        Should.Throw<InvalidOperationException>(
            () => services.ValidateAlertWindowFitsRetention(Configuration("60", "60")));
    }

    /// <summary>The failure the check exists for, and the message must say which key to raise.</summary>
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

    /// <summary>Neither key set. The two fallback constants are hand-copied from two modules' options,
    /// so nothing but this test says they still agree with each other.</summary>
    [Fact]
    public void ValidateAlertWindowFitsRetention_NeitherKeyIsConfigured_DoesNotThrow()
    {
        var services = new ServiceCollection();

        Should.NotThrow(() => services.ValidateAlertWindowFitsRetention(Configuration(null, null)));
    }

    /// <summary>A zero is not a value, it is a missing one. Read it literally and the host refuses to
    /// start on a placeholder that every other reader of this key ignores.</summary>
    [Fact]
    public void ValidateAlertWindowFitsRetention_RetentionIsZero_FallsBackToTheDefaultAndDoesNotThrow()
    {
        var services = new ServiceCollection();

        Should.NotThrow(() => services.ValidateAlertWindowFitsRetention(Configuration("0", null)));
    }

    /// <summary>Not a made-up pair: the numbers the repository actually ships. Raising the window in
    /// appsettings.json without raising retention is the exact mistake this check was written for.</summary>
    [Fact]
    public void ValidateAlertWindowFitsRetention_TheShippedAppSettings_DoesNotThrow()
    {
        var path = Path.Combine(RepositoryPaths.Root, "src", "Api", "appsettings.json");

        File.Exists(path).ShouldBeTrue(
            $"'{path}' is what the host loads at startup. If this test cannot find it, the assertion "
                + "below reads an empty configuration and passes on the fallback constants instead.");

        var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();

        // A missing key here would silently fall back, so prove both are present before comparing them.
        configuration[RetentionKey].ShouldNotBeNullOrEmpty();
        configuration[MaxWindowKey].ShouldNotBeNullOrEmpty();

        Should.NotThrow(() => new ServiceCollection().ValidateAlertWindowFitsRetention(configuration));
    }

    /// <summary>Builds a configuration from the two keys; null means the key is absent altogether.</summary>
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
