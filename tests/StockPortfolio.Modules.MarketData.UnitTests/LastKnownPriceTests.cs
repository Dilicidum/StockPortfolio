using Microsoft.Extensions.Time.Testing;
using Shouldly;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Tests;

public sealed class LastKnownPriceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 15, 0, 0, TimeSpan.Zero);

    private static bool IsShown(LastPrice price, DateTimeOffset now) =>
        LastKnownPrice.IsWorthShowing(price, new FakeTimeProvider(now).GetUtcNow());

    [Fact]
    public void LastKnown_WeekendPriceFromFridaysClose_IsStillShown()
    {
        var price = new LastPrice(187.42m, new DateTimeOffset(2026, 7, 31, 20, 0, 0, TimeSpan.Zero));

        IsShown(price, new DateTimeOffset(2026, 8, 2, 18, 0, 0, TimeSpan.Zero)).ShouldBeTrue();
    }

    [Fact]
    public void LastKnown_OvernightPriceFromThePreviousClose_IsStillShown()
    {
        var price = new LastPrice(187.42m, new DateTimeOffset(2026, 8, 3, 20, 0, 0, TimeSpan.Zero));

        IsShown(price, new DateTimeOffset(2026, 8, 4, 7, 0, 0, TimeSpan.Zero)).ShouldBeTrue();
    }

    [Fact]
    public void LastKnown_MidSessionPriceFortyFiveMinutesOld_IsStillShown()
    {
        var price = new LastPrice(187.42m, Now - TimeSpan.FromMinutes(45));

        IsShown(price, Now).ShouldBeTrue();
    }

    [Fact]
    public void LastKnown_MidSessionPriceNinetyMinutesOld_IsNotShown()
    {
        var price = new LastPrice(187.42m, Now - TimeSpan.FromMinutes(90));

        IsShown(price, Now).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LastKnown_ZeroOrNegativePrice_IsNotShown(decimal amount)
    {
        var price = new LastPrice(amount, Now - TimeSpan.FromMinutes(1));

        IsShown(price, Now).ShouldBeFalse();
    }

    [Fact]
    public void LastKnown_PriceStampedAnHourAhead_IsNotShown()
    {
        var price = new LastPrice(187.42m, Now + TimeSpan.FromHours(1));

        IsShown(price, Now).ShouldBeFalse();
    }
}
