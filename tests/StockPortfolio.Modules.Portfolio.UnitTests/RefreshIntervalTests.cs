using Shouldly;
using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Tests;

public sealed class RefreshIntervalTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(300)]
    public void Create_InRange_Succeeds(int seconds) =>
        RefreshInterval.Create(seconds).AsT0.Seconds.ShouldBe(seconds);

    [Theory]
    [InlineData(9)]
    [InlineData(301)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_OutOfRange_IsInvalidInput(int seconds) =>
        RefreshInterval.Create(seconds).IsT1.ShouldBeTrue();

    [Fact]
    public void Default_IsSixtySeconds() => RefreshInterval.Default.Seconds.ShouldBe(60);
}
