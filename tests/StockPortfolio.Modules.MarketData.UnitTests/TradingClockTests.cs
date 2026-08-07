using Shouldly;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Tests;

public sealed class TradingClockTests
{
    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void OpenMinutes_ToBeforeFrom_IsZero() =>
        TradingClock.OpenMinutesBetween(Utc(2026, 8, 4, 15, 0), Utc(2026, 8, 4, 14, 0)).ShouldBe(0d);

    [Fact]
    public void OpenMinutes_SundayAfternoonAfterFridaysClose_IsZero() =>
        TradingClock.OpenMinutesBetween(Utc(2026, 7, 31, 20, 0), Utc(2026, 8, 2, 18, 0)).ShouldBe(0d);

    [Fact]
    public void OpenMinutes_TuesdayBeforeDawnAfterMondaysClose_IsZero() =>
        TradingClock.OpenMinutesBetween(Utc(2026, 8, 3, 20, 0), Utc(2026, 8, 4, 7, 0)).ShouldBe(0d);

    [Fact]
    public void OpenMinutes_NinetyMinutesIntoATuesdaySession_IsNinety() =>
        TradingClock.OpenMinutesBetween(Utc(2026, 8, 4, 13, 30), Utc(2026, 8, 4, 15, 0)).ShouldBe(90d);

    [Fact]
    public void OpenMinutes_AWholeWeekdaySession_IsThreeHundredAndNinety() =>
        TradingClock.OpenMinutesBetween(Utc(2026, 8, 4, 0, 0), Utc(2026, 8, 5, 0, 0)).ShouldBe(390d);

    // January is EST, so 13:30 UTC is still an hour before the bell; reading it as EDT would score 60.
    [Fact]
    public void OpenMinutes_InJanuaryTheHourBeforeHalfPastTwo_IsZero() =>
        TradingClock.OpenMinutesBetween(Utc(2026, 1, 20, 13, 30), Utc(2026, 1, 20, 14, 30)).ShouldBe(0d);

    [Fact]
    public void OpenMinutes_InJanuaryTheHourAfterHalfPastTwo_IsSixty() =>
        TradingClock.OpenMinutesBetween(Utc(2026, 1, 20, 14, 30), Utc(2026, 1, 20, 15, 30)).ShouldBe(60d);

    // July is EDT, so the same 13:30 UTC is the bell; reading it as EST would score 0.
    [Fact]
    public void OpenMinutes_InJulyTheHourAfterHalfPastOne_IsSixty() =>
        TradingClock.OpenMinutesBetween(Utc(2026, 7, 14, 13, 30), Utc(2026, 7, 14, 14, 30)).ShouldBe(60d);

    [Fact]
    public void OpenMinutes_InJulyTheHourAfterEightUtc_IsZero() =>
        TradingClock.OpenMinutesBetween(Utc(2026, 7, 14, 20, 0), Utc(2026, 7, 14, 21, 0)).ShouldBe(0d);

    // Friday 11:00 to Monday 11:00 New York: 300 minutes of Friday plus 90 of Monday, not 4,320 of wall clock.
    [Fact]
    public void OpenMinutes_AcrossAWholeWeekend_CountsOnlyTheWeekdayMinutes() =>
        TradingClock.OpenMinutesBetween(Utc(2026, 7, 31, 15, 0), Utc(2026, 8, 3, 15, 0)).ShouldBe(390d);
}
