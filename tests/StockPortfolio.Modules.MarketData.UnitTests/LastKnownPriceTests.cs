using Microsoft.Extensions.Time.Testing;
using Shouldly;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Tests;

public sealed class LastKnownPriceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LastKnown_FiveDayOldPrice_IsStillShown()
    {
        var clock = new FakeTimeProvider(Now);
        var price = new LastPrice(187.42m, Now - TimeSpan.FromDays(5));

        LastKnownPrice.IsWorthShowing(price, clock.GetUtcNow()).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LastKnown_ZeroOrNegativePrice_IsNotShown(decimal amount)
    {
        var clock = new FakeTimeProvider(Now);
        var price = new LastPrice(amount, Now - TimeSpan.FromMinutes(1));

        LastKnownPrice.IsWorthShowing(price, clock.GetUtcNow()).ShouldBeFalse();
    }

    [Fact]
    public void LastKnown_PriceStampedAnHourAhead_IsNotShown()
    {
        var clock = new FakeTimeProvider(Now);
        var price = new LastPrice(187.42m, Now + TimeSpan.FromHours(1));

        LastKnownPrice.IsWorthShowing(price, clock.GetUtcNow()).ShouldBeFalse();
    }
}
