using Microsoft.Extensions.Logging;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Application.Streaming;
using StockPortfolio.Modules.Alerts.Contracts;
using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Alerts.Application.Evaluation;

/// <summary>Judges one ticker's thresholds against its price window, once per fresh sample.</summary>
public sealed partial class AlertEvaluator(
    IAlertSettingRepository settings,
    IPriceWindowReader windows,
    IAlertCooldownStore cooldowns,
    AlertDispatcher dispatcher,
    AlertsOptions options,
    TimeProvider clock,
    ILogger<AlertEvaluator> logger) : IAlertEvaluator
{
    /// <inheritdoc/>
    public async Task EvaluateAsync(string ticker, CancellationToken ct)
    {
        if (!Ticker.Create(ticker).TryPickT0(out var symbol, out _))
        {
            return;
        }

        var watching = await settings.ListEnabledForTickerAsync(symbol.Value, ct);

        if (watching.Count == 0)
        {
            return;
        }

        // One read per distinct window length, not one for the longest. Every setting judged against
        // the longest window would silently widen a five-minute threshold to an hour the moment any
        // other user asked for one, and the two users never meet anywhere else.
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

    /// <summary>The three guards of the phase plan, in the order they are cheapest and least noisy.</summary>
    private bool Usable(PriceWindow window, string ticker, ref bool staleLogged)
    {
        // Too few points is the ordinary state of a ticker somebody has just started watching, so it
        // is silent. A rule that logs its own warm-up is a rule people turn the log level down on.
        if (window.SampleCount < options.MinimumSamples)
        {
            return false;
        }

        // A window straddling a period when nothing was sampled - a weekend, or an hour the provider
        // was unreachable - compares two prices that never faced each other. No calendar needed.
        if (window.LargestGap > options.MaxSampleGap)
        {
            return false;
        }

        // A feed that stopped is not a price that stopped moving. Suppressing here and logging is the
        // whole of the feed-health signal: the user asked about a price, not about the pipeline.
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

        // Set-if-absent, so two replicas evaluating the same fresh sample produce one alert between
        // them. A read followed by a write would let both pass in the same millisecond.
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

        // Persist, then publish — and the order lives in the dispatcher, not here, because Simulate
        // sends an alert down the same path and the two must not be able to disagree about it.
        await dispatcher.DispatchAsync(alert, ct);
    }

    [LoggerMessage(
        EventId = 5310,
        Level = LogLevel.Warning,
        Message = "Price alerts for {Ticker} are suppressed: the newest sample is from {NewestAt} and the "
            + "feed is stale. A stale feed is not a price that stopped moving")]
    private static partial void LogStaleFeed(ILogger logger, string ticker, DateTimeOffset newestAt);
}
