using Shouldly;
using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Tests;

public sealed class FiredAlertTests
{
    private static readonly Guid User = Guid.CreateVersion7();

    private static readonly DateTimeOffset FiredAt =
        new(2026, 8, 6, 14, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(AlertDirection.Fall, -5.33, -2.07)]
    [InlineData(AlertDirection.Rise, 6.43, 6.43)]
    public void Record_StoresTheSignOfEachMoveAsGiven(
        AlertDirection direction,
        decimal changePercent,
        decimal endpointPercent)
    {
        var alert = Record(direction, changePercent, endpointPercent);

        alert.Direction.ShouldBe(direction);
        alert.ChangePercent.ShouldBe(changePercent);
        alert.EndpointPercent.ShouldBe(endpointPercent);
    }

    [Fact]
    public void Record_KeepsBothPricesAndTheirCurrency()
    {
        var alert = Record(AlertDirection.Fall, -5.33m, -2.07m);

        alert.TriggerPrice.ShouldBe(new Money(142m, "USD"));
        alert.ReferencePrice.ShouldBe(new Money(150m, "USD"));
        alert.TriggerPrice.Currency.ShouldBe("USD");
        alert.ReferencePrice.Currency.ShouldBe("USD");
    }

    [Fact]
    public void Record_KeepsTheInstantAndTheSimulationFlag()
    {
        Record(AlertDirection.Rise, 6m, 6m).FiredAt.ShouldBe(FiredAt);
        Record(AlertDirection.Rise, 6m, 6m, isSimulated: true).IsSimulated.ShouldBeTrue();
        Record(AlertDirection.Rise, 6m, 6m).IsSimulated.ShouldBeFalse();
    }

    [Fact]
    public void Record_GivesEveryAlertItsOwnId() =>
        Record(AlertDirection.Rise, 6m, 6m).Id.ShouldNotBe(Record(AlertDirection.Rise, 6m, 6m).Id);

    private static FiredAlert Record(
        AlertDirection direction,
        decimal changePercent,
        decimal endpointPercent,
        bool isSimulated = false) =>
        FiredAlert.Record(
            User,
            Ticker.Create("AAPL").AsT0,
            direction,
            changePercent,
            endpointPercent,
            Money.Usd(142m),
            Money.Usd(150m),
            FiredAt,
            isSimulated);
}
