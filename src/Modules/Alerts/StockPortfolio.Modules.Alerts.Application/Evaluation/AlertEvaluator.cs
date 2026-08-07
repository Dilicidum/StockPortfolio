using Microsoft.Extensions.Logging;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Application.Streaming;
using StockPortfolio.Modules.Alerts.Contracts;
using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Alerts.Application.Evaluation;

public sealed partial class AlertEvaluator(
    IAlertSettingRepository settings,
    IPriceWindowReader windows,
    IAlertCooldownStore cooldowns,
    AlertDispatcher dispatcher,
    AlertsOptions options,
    TimeProvider clock,
    ILogger<AlertEvaluator> logger) : IAlertEvaluator
{
    public Task EvaluateAsync(string ticker, CancellationToken ct) =>
        Ticker.Create(ticker).Match(
            symbol => EvaluateAsync(symbol, ct),
            badTicker => Task.CompletedTask);

    private async Task EvaluateAsync(Ticker symbol, CancellationToken ct)
    {
        var watching = await settings.ListEnabledForTickerAsync(symbol.Value, ct);

        if (watching.Count == 0)
        {
            return;
        }

        var staleLogged = false;

        foreach (var group in watching.GroupBy(setting => setting.Window.Minutes).OrderBy(group => group.Key))
        {
            var window = await windows.GetWindowAsync(symbol.Value, TimeSpan.FromMinutes(group.Key), ct);

            if (window is null || !Usable(window, symbol.Value, ref staleLogged))
            {
                continue;
            }

            foreach (var setting in group)
            {
                await ConsiderAsync(setting, window, ct);
            }
        }
    }

    private bool Usable(PriceWindow window, string ticker, ref bool staleLogged)
    {
        if (window.SampleCount < options.MinimumSamples)
        {
            return false;
        }

        if (window.LargestGap > options.MaxSampleGap)
        {
            return false;
        }

        // A feed that stopped is not a price that stopped moving, so alerts are suppressed rather than fired.
        if (clock.GetUtcNow() - window.NewestAt > options.MaxSampleGap)
        {
            if (!staleLogged)
            {
                staleLogged = true;
                LogStaleFeed(logger, ticker, window.NewestAt);
            }

            return false;
        }

        return true;
    }

    private async Task ConsiderAsync(AlertSetting setting, PriceWindow window, CancellationToken ct)
    {
        var verdict = MoveAssessment.Assess(window, setting.Threshold.Value);

        if (!verdict.Fires)
        {
            return;
        }

        if (!await cooldowns.TryStartAsync(
                setting.UserId,
                setting.Ticker.Value,
                verdict.Direction,
                options.Cooldown,
                ct))
        {
            return;
        }

        var alert = FiredAlert.Record(
            setting.UserId,
            setting.Ticker,
            verdict.Direction,
            verdict.ExtremePercent,
            verdict.EndpointPercent,
            Money.Usd(window.Current),
            Money.Usd(verdict.ReferencePrice),
            clock.GetUtcNow(),
            isSimulated: false);

        await dispatcher.DispatchAsync(alert, ct);
    }

    [LoggerMessage(
        EventId = 5310,
        Level = LogLevel.Warning,
        Message = "Price alerts for {Ticker} are suppressed: the newest sample is from {NewestAt} and the "
            + "feed is stale. A stale feed is not a price that stopped moving")]
    private static partial void LogStaleFeed(ILogger logger, string ticker, DateTimeOffset newestAt);
}
