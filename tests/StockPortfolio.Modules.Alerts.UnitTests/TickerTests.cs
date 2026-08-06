using Shouldly;
using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Tests;

/// <summary>Alerts' own Ticker. The rules are written out rather than shared: Portfolio's type is off-limits.</summary>
public sealed class TickerTests
{
    [Theory]
    [InlineData("aapl", "AAPL")]
    [InlineData("  msft  ", "MSFT")]
    [InlineData("F", "F")]
    [InlineData("GOOGL", "GOOGL")]
    public void Create_Normalises_ToTrimmedUppercase(string input, string expected) =>
        Ticker.Create(input).AsT0.Value.ShouldBe(expected);

    [Theory]
    [InlineData("TOOLONG")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("AA PL")]
    [InlineData("BRK.B")]
    [InlineData("AAPL1")]
    [InlineData("'; DROP TABLE alerts.alert_settings; --")]
    public void Create_RejectsAnythingOutsideTheShape(string? input) =>
        Ticker.Create(input).AsT1.Field.ShouldBe("ticker");

    [Fact]
    public void Equality_IsOrdinal_OnTheNormalisedValue() =>
        Ticker.Create("aapl").AsT0.ShouldBe(Ticker.Create("AAPL").AsT0);

    [Fact]
    public void ToString_IsTheSymbolItself() =>
        Ticker.Create("nvda").AsT0.ToString().ShouldBe("NVDA");
}
