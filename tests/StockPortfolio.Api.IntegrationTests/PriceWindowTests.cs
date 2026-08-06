using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Api.IntegrationTests.Infrastructure;
using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>The price window against a real Redis, which is the only place the sorted set's own rules apply.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class PriceWindowTests(ApiFixture fixture)
{
    private static readonly DateTimeOffset Origin = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(75);

    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    private IPriceWindowStore Store => _fixture.Services.GetRequiredService<IPriceWindowStore>();

    [Fact]
    public async Task Window_SamePriceTwice_KeepsBothReadings()
    {
        // A sorted set keys on the member, so encoding the bare price makes the second write a score
        // UPDATE of the first entry. The series then silently loses a reading and the count still looks
        // plausible. Only a real Redis can fail this one; a fake dictionary of members cannot.
        const string Ticker = "WNDUP";

        await Store.AppendAsync(Ticker, 187.42m, Origin, Retention, Ct);
        await Store.AppendAsync(Ticker, 187.42m, Origin.AddMinutes(1), Retention, Ct);
        await Store.AppendAsync(Ticker, 187.42m, Origin.AddMinutes(2), Retention, Ct);

        var samples = await Store.ReadAsync(Ticker, Origin.AddMinutes(-60), Ct);

        samples.Count.ShouldBe(3);
        samples.Select(sample => sample.At).ShouldBe(
            [Origin, Origin.AddMinutes(1), Origin.AddMinutes(2)]);
        samples.ShouldAllBe(sample => sample.Price == 187.42m);
    }

    [Fact]
    public async Task Window_ReadsOldestFirst_WhateverOrderTheSamplesArrivedIn()
    {
        const string Ticker = "WNORD";

        await Store.AppendAsync(Ticker, 150m, Origin.AddMinutes(2), Retention, Ct);
        await Store.AppendAsync(Ticker, 141m, Origin, Retention, Ct);
        await Store.AppendAsync(Ticker, 149m, Origin.AddMinutes(1), Retention, Ct);

        var samples = await Store.ReadAsync(Ticker, Origin.AddMinutes(-60), Ct);

        samples.Select(sample => sample.Price).ShouldBe([141m, 149m, 150m]);
    }

    [Fact]
    public async Task Window_Append_DropsWhateverFellOutOfRetention()
    {
        const string Ticker = "WNTRM";

        var retention = TimeSpan.FromMinutes(10);

        await Store.AppendAsync(Ticker, 100m, Origin, retention, Ct);
        await Store.AppendAsync(Ticker, 101m, Origin.AddMinutes(5), retention, Ct);

        // Eleven minutes on, the first sample is older than retention and the write that follows must
        // take it out — otherwise the series grows without bound for any ticker anyone ever watched.
        await Store.AppendAsync(Ticker, 102m, Origin.AddMinutes(11), retention, Ct);

        var samples = await Store.ReadAsync(Ticker, Origin.AddYears(-1), Ct);

        samples.Select(sample => sample.Price).ShouldBe([101m, 102m]);
    }

    [Fact]
    public async Task Window_Read_IgnoresAnythingBeforeTheWindowStarts()
    {
        const string Ticker = "WNCUT";

        await Store.AppendAsync(Ticker, 100m, Origin, Retention, Ct);
        await Store.AppendAsync(Ticker, 101m, Origin.AddMinutes(30), Retention, Ct);

        var samples = await Store.ReadAsync(Ticker, Origin.AddMinutes(15), Ct);

        samples.Select(sample => sample.Price).ShouldBe([101m]);
    }

    [Fact]
    public async Task Window_TickerNobodyHasSampled_IsEmpty()
    {
        var samples = await Store.ReadAsync("WNNIL", Origin.AddMinutes(-60), Ct);

        samples.ShouldBeEmpty();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
