using Shouldly;

using StockPortfolio.Modules.Alerts.Api.Requests;
using StockPortfolio.Modules.Alerts.Api.Validators;

namespace StockPortfolio.Tests;

public sealed class RequestValidatorTests
{
    private static readonly SaveAlertSettingRequestValidator Save = new();

    [Theory]
    [InlineData("AAPL", 5, 30)]
    [InlineData("aapl", 0.01, 1)]
    [InlineData("F", 100, 1440)]
    public void Save_AcceptsAWellFormedThreshold(string ticker, decimal percent, int window) =>
        Save.Validate(new SaveAlertSettingRequest(ticker, percent, window, true)).IsValid.ShouldBeTrue();

    // A day-long window is accepted here on purpose: the cap is configuration, so refusing it is the handler's 409.
    [Theory]
    [InlineData("TOOLONG", 5, 30, "Ticker")]
    [InlineData("", 5, 30, "Ticker")]
    [InlineData("BRK.B", 5, 30, "Ticker")]
    [InlineData("'; DROP TABLE alerts.alert_settings; --", 5, 30, "Ticker")]
    [InlineData("AAPL", 0, 30, "ThresholdPercent")]
    [InlineData("AAPL", -5, 30, "ThresholdPercent")]
    [InlineData("AAPL", 100.01, 30, "ThresholdPercent")]
    [InlineData("AAPL", 5, 0, "WindowMinutes")]
    [InlineData("AAPL", 5, -1, "WindowMinutes")]
    public void Save_RejectsAndNamesTheField(string ticker, decimal percent, int window, string field) =>
        Save.Validate(new SaveAlertSettingRequest(ticker, percent, window, true))
            .Errors.Select(error => error.PropertyName)
            .ShouldContain(field);
}
