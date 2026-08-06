using Microsoft.Extensions.Configuration;
using Shouldly;

using StockPortfolio.Modules.MarketData.Infrastructure.Polling;

namespace StockPortfolio.Tests;

public sealed class PollingOptionsTests
{
    private static PollingOptions Read(params (string Key, string Value)[] settings) =>
        PollingOptions.FromConfiguration(
            new ConfigurationBuilder()
                .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
                .Build());

    [Fact]
    public void Options_WithNothingConfigured_AreTheDocumentedDefaults()
    {
        var options = Read();

        options.Interval.ShouldBe(TimeSpan.FromSeconds(60));
        options.Retention.ShouldBe(TimeSpan.FromMinutes(75));
    }

    [Fact]
    public void Options_ReadTheColonPathTheDeploymentActuallySets()
    {
        // MarketData:Polling:* is a two-level path, and GetSection has to walk both. A section name that
        // silently matched nothing would hand back the defaults and look exactly like a correct read.
        var options = Read(
            ("MarketData:Polling:IntervalSeconds", "30"),
            ("MarketData:Polling:RetentionMinutes", "90"));

        options.Interval.ShouldBe(TimeSpan.FromSeconds(30));
        options.Retention.ShouldBe(TimeSpan.FromMinutes(90));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sixty")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("60.5")]
    public void Options_UnusableInterval_FallsBackRatherThanThrowing(string configured)
    {
        // Nothing here throws, on purpose: a typo in one deployment variable must not stop the host, and a
        // zero interval would spin the poll loop as fast as the machine allows.
        Read(("MarketData:Polling:IntervalSeconds", configured))
            .Interval.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Options_UnusableRetention_FallsBackToo() =>
        Read(("MarketData:Polling:RetentionMinutes", "nope")).Retention.ShouldBe(TimeSpan.FromMinutes(75));
}
