using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using StackExchange.Redis;

using StockPortfolio.Modules.MarketData.Infrastructure.Polling;

namespace StockPortfolio.Tests;

public sealed class RedisPollLeaseTests
{
    private static readonly DateTimeOffset Utc = new(2026, 8, 6, 14, 37, 41, TimeSpan.Zero);

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Fact]
    public void ClaimKey_IsOneKeyPerCycle()
    {
        RedisPollLease.ClaimKey(Utc.AddSeconds(18), Minute).ShouldBe(RedisPollLease.ClaimKey(Utc, Minute));
        RedisPollLease.ClaimKey(Utc.AddMinutes(1), Minute).ShouldNotBe(RedisPollLease.ClaimKey(Utc, Minute));
    }

    [Fact]
    public void ClaimKey_IsNamedByTheIntervalNotTheCalendarMinute()
    {
        // Named by the calendar minute, a claim living under 30s expires inside the minute it names and a second replica claims it.
        var fifteen = TimeSpan.FromSeconds(15);

        RedisPollLease.ClaimKey(Utc.AddSeconds(18), fifteen)
            .ShouldNotBe(RedisPollLease.ClaimKey(Utc, fifteen));
    }

    [Fact]
    public void ClaimKey_IsTheSameInstantEverywhere_NotTheLocalClockFace()
    {
        // A key built off the local clock face would let a replica in another zone claim its own cycle, and both would poll.
        RedisPollLease.ClaimKey(Utc.ToOffset(TimeSpan.FromHours(5)), Minute)
            .ShouldBe(RedisPollLease.ClaimKey(Utc, Minute));
    }

    [Fact]
    public async Task Lease_RedisUnreachable_SkipsTheCycleInsteadOfThrowing()
    {
        await using var multiplexer = await DeadMultiplexerAsync();

        var lease = new RedisPollLease(
            multiplexer, Options(), NullLogger<RedisPollLease>.Instance);

        // False, not a throw and not true: an unreachable Redis has nowhere to put a sample, so the cycle must not run.
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
