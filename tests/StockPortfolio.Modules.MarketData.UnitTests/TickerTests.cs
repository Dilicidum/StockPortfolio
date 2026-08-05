using Shouldly;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Tests;

public sealed class TickerTests
{
    [Theory]
    [InlineData("aapl", "AAPL")]
    [InlineData("  msft  ", "MSFT")]
    [InlineData("F", "F")]
    public void Ticker_LowerCase_CanonicalisesToUpper(string input, string expected) =>
        Ticker.Create(input).AsT0.Value.ShouldBe(expected);

    [Theory]
    [InlineData("TOOLONG")]
    [InlineData("AAPL1")]
    [InlineData("BRK.B")]
    public void Ticker_TooLong_ReturnsInvalidInput(string input) =>
        Ticker.Create(input).AsT1.Field.ShouldBe("ticker");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Ticker_Empty_ReturnsInvalidInput(string? input) =>
        Ticker.Create(input).AsT1.Field.ShouldBe("ticker");
}
