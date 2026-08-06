using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using StackExchange.Redis;

using StockPortfolio.Modules.MarketData.Infrastructure.Polling;

namespace StockPortfolio.Tests;

public sealed class RedisPollLeaseTests
{
    private static readonly DateTimeOffset Utc = new(2026, 8, 6, 14, 37, 41, TimeSpan.Zero);

    [Fact]
    public void ClaimKey_IsOneKeyPerMinute()
    {
        // The whole point of this key: two replicas that wake in the same minute must compute the same
        // string, and the same replica a minute later must not.
        RedisPollLease.ClaimKey(Utc).ShouldBe("marketdata:claim:202608061437");
        RedisPollLease.ClaimKey(Utc.AddSeconds(18)).ShouldBe(RedisPollLease.ClaimKey(Utc));
        RedisPollLease.ClaimKey(Utc.AddMinutes(1)).ShouldNotBe(RedisPollLease.ClaimKey(Utc));
    }

    [Fact]
    public void ClaimKey_IsTheUtcMinuteNotTheLocalOne()
    {
        // Same instant, written with a +05:00 offset. A key built off the local clock face would let a
        // replica in another zone claim its own minute and both would poll.
        RedisPollLease.ClaimKey(Utc.ToOffset(TimeSpan.FromHours(5)))
            .ShouldBe(RedisPollLease.ClaimKey(Utc));
    }

    [Fact]
    public async Task Lease_RedisUnreachable_SkipsTheCycleInsteadOfThrowing()
    {
        await using var multiplexer = await DeadMultiplexerAsync();

        var lease = new RedisPollLease(
            multiplexer, Options(), NullLogger<RedisPollLease>.Instance);

        // False, not a throw and not true: an unreachable Redis has nowhere to put a sample, so running the
        // cycle anyway would burn provider budget for nothing.
        (await lease.TryAcquireAsync(Utc, TestContext.Current.CancellationToken)).ShouldBeFalse();

        await Should.NotThrowAsync(() => lease.ReleaseAsync(TestContext.Current.CancellationToken));
    }

    private static PollingOptions Options() =>
        PollingOptions.FromConfiguration(new ConfigurationBuilder().Build());

    private static Task<ConnectionMultiplexer> DeadMultiplexerAsync()
    {
        var options = ConfigurationOptions.Parse("127.0.0.1:1");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 50;
        options.ConnectRetry = 1;
        options.SyncTimeout = 50;

        return ConnectionMultiplexer.ConnectAsync(options);
    }
}
