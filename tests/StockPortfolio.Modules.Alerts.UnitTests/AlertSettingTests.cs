using Shouldly;
using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Tests;

public sealed class AlertSettingTests
{
    private const int MaxWindowMinutes = 60;

    private static readonly Guid User = Guid.CreateVersion7();

    [Fact]
    public void Create_KeepsEveryValueItWasGiven()
    {
        var setting = Valid();

        setting.UserId.ShouldBe(User);
        setting.Ticker.Value.ShouldBe("AAPL");
        setting.Threshold.Value.ShouldBe(5m);
        setting.Window.Minutes.ShouldBe(30);
        setting.Enabled.ShouldBeTrue();
        setting.Id.Value.ShouldNotBe(Guid.Empty);
    }

    [Theory]
    [InlineData("BRK.B", 5, 30, "ticker")]
    [InlineData("AAPL", 0, 30, "thresholdPercent")]
    [InlineData("AAPL", 5, 61, "windowMinutes")]
    public void Create_ReportsTheFieldThatFailed(
        string ticker,
        decimal percent,
        int windowMinutes,
        string expectedField) =>
        AlertSetting.Create(User, ticker, percent, windowMinutes, enabled: true, MaxWindowMinutes)
            .AsT1.Field.ShouldBe(expectedField);

    [Fact]
    public void Create_GivesEverySettingItsOwnId() =>
        Valid().Id.ShouldNotBe(Valid().Id);

    [Fact]
    public void Adjust_ChangesThresholdWindowAndEnabledTogether()
    {
        var setting = Valid();

        setting.Adjust(12.5m, 45, enabled: false, MaxWindowMinutes).IsT0.ShouldBeTrue();

        setting.Threshold.Value.ShouldBe(12.5m);
        setting.Window.Minutes.ShouldBe(45);
        setting.Enabled.ShouldBeFalse();
    }

    [Theory]
    [InlineData(-1, 45, "thresholdPercent")]
    [InlineData(12.5, 61, "windowMinutes")]
    public void Adjust_LeavesTheEntityUntouched_WhenAnyValueIsBad(
        decimal percent,
        int windowMinutes,
        string expectedField)
    {
        var setting = Valid();

        setting.Adjust(percent, windowMinutes, enabled: false, MaxWindowMinutes)
            .AsT1.Field.ShouldBe(expectedField);

        // Validation happens before a single assignment: a half-applied change is the failure this pins.
        setting.Threshold.Value.ShouldBe(5m);
        setting.Window.Minutes.ShouldBe(30);
        setting.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void Adjust_JudgesTheWindowAgainstTheCapItIsGiven() =>
        Valid().Adjust(5m, 90, enabled: true, maxWindowMinutes: 120).IsT0.ShouldBeTrue();

    private static AlertSetting Valid() =>
        AlertSetting.Create(User, "aapl", 5m, 30, enabled: true, MaxWindowMinutes).AsT0;
}
