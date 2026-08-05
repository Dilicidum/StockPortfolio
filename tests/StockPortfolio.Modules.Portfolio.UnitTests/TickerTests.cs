using Shouldly;
using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Tests;

public sealed class TickerTests
{
    [Theory]
    [InlineData("aapl", "AAPL")]
    [InlineData("  msft  ", "MSFT")]
    [InlineData("F", "F")]
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
    [InlineData("'; DROP TABLE portfolio.holdings; --")]
    public void Create_RejectsAnythingOutsideTheShape(string? input) =>
        Ticker.Create(input).AsT1.Field.ShouldBe("ticker");

    [Fact]
    public void Equality_IsOrdinal_OnTheNormalisedValue() =>
        Ticker.Create("aapl").AsT0.ShouldBe(Ticker.Create("AAPL").AsT0);
}
