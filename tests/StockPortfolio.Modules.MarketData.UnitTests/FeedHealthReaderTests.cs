using Shouldly;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;
using StockPortfolio.Modules.MarketData.Infrastructure.Health;
using StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

namespace StockPortfolio.Tests;

public sealed class FeedHealthReaderTests
{
    private static readonly DateTimeOffset Finished = new(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Read_WithAStoredHeartbeat_ReportsAllThreeOfItsFacts()
    {
        var health = await Build(new PollHeartbeat(Finished, 7, 5)).GetFeedHealthAsync(Ct);

        health.LastCycleAt.ShouldBe(Finished);
        health.TickersTargeted.ShouldBe(7);
        health.TickersStored.ShouldBe(5);
        health.ProviderName.ShouldBe("Stub");
        health.ProviderKeyRejected.ShouldBeFalse();
    }

    [Fact]
    public async Task Read_WithNoHeartbeatAtAll_ReportsNoCycleRatherThanAnInventedOne()
    {
        var health = await Build(heartbeat: null).GetFeedHealthAsync(Ct);

        // Null, not "now": a stamped default would read as a cycle that just finished.
        health.LastCycleAt.ShouldBeNull();
        health.TickersTargeted.ShouldBe(0);
    }

    [Fact]
    public async Task Read_AfterTheProviderRefusedTheApplicationsKey_SaysSo()
    {
        var rejection = new ProviderKeyRejection();
        rejection.Raise();

        (await Build(new PollHeartbeat(Finished, 7, 5), rejection).GetFeedHealthAsync(Ct))
            .ProviderKeyRejected.ShouldBeTrue();
    }

    private static FeedHealthReader Build(PollHeartbeat? heartbeat, ProviderKeyRejection? rejection = null) =>
        new(new StubHeartbeatStore(heartbeat), new StubProvider(), rejection ?? new ProviderKeyRejection());

    private sealed class StubHeartbeatStore(PollHeartbeat? heartbeat) : IPollHeartbeatStore
    {
        public Task WriteAsync(PollHeartbeat written, CancellationToken ct) => Task.CompletedTask;

        public Task<PollHeartbeat?> ReadAsync(CancellationToken ct) => Task.FromResult(heartbeat);
    }

    private sealed class StubProvider : IQuoteProvider
    {
        public string Name => "Stub";

        public Task<IReadOnlyList<Quote>> GetQuotesAsync(
            IReadOnlySet<Ticker> tickers, string? apiKeyOverride, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Quote>>([]);

        public Task<bool> SymbolExistsAsync(Ticker ticker, CancellationToken ct) => Task.FromResult(true);

        public Task<IReadOnlyList<SymbolMatch>> SearchSymbolsAsync(string query, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SymbolMatch>>([]);

        public Task<KeyVerdict> VerifyKeyAsync(string apiKey, CancellationToken ct) =>
            Task.FromResult(KeyVerdict.Accepted);
    }
}
