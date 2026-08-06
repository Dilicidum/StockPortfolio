using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Polling;

/// <summary>The only background job in the application: it samples the tickers an alert is watching.</summary>
internal sealed partial class QuotePoller(
    IServiceScopeFactory scopeFactory,
    IPollLease lease,
    IPriceWindowStore windowStore,
    ILastKnownPriceStore lastKnownStore,
    PollingOptions options,
    TimeProvider clock,
    ILogger<QuotePoller> logger) : BackgroundService
{
    /// <summary>One cycle. Internal so a test can run exactly one without driving the timer.</summary>
    internal async Task RunCycleAsync(CancellationToken ct)
    {
        if (!await lease.TryAcquireAsync(clock.GetUtcNow(), ct))
        {
            return;
        }

        try
        {
            await PollAsync(ct);
        }
        finally
        {
            // CancellationToken.None: a host on its way down must still hand the in-flight flag back, or
            // every replica waits out the expiry before any cycle can run again.
            await lease.ReleaseAsync(CancellationToken.None);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Interval, clock);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunCycleSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The host is stopping, and a cancelled wait is the ordinary way this service ends.
        }
    }

    private async Task RunCycleSafelyAsync(CancellationToken ct)
    {
        // The catch goes INSIDE the loop, never around it: an unhandled exception out of a BackgroundService
        // takes the whole host down, because StopHost is the default BackgroundServiceExceptionBehavior.
        try
        {
            await RunCycleAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogCycleFailed(logger, ex);
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        // A scope per cycle. The poller is a singleton; the target source and the observer are the host's
        // adapters over Alerts and are scoped, and the typed Finnhub client is transient, so capturing any
        // of the three on this object would outlive what it is allowed to.
        await using var scope = scopeFactory.CreateAsyncScope();

        var targets = await scope.ServiceProvider
            .GetRequiredService<IPollTargetSource>()
            .GetPollTargetsAsync(ct);

        var tickers = Canonicalise(targets);

        // Returns before the provider is touched at all: with no alerts configured, nothing is polled.
        if (tickers.Count == 0)
        {
            return;
        }

        // null: the poller has no user, so it has no key to pass. The shared window is shared, and a
        // user's own quota must not be spent filling it.
        var quotes = await scope.ServiceProvider
            .GetRequiredService<IQuoteProvider>()
            .GetQuotesAsync(tickers, apiKeyOverride: null, ct);

        if (quotes.Count == 0)
        {
            return;
        }

        // Through the same store the dashboard writes, not a second encoder: the window is what alerts read
        // and the last-known key is what the dashboard falls back to, so a sample landing in one and not the
        // other diverges with nothing to show for it.
        await lastKnownStore.WriteAsync(quotes, ct);

        var observer = scope.ServiceProvider.GetRequiredService<IPriceSampleObserver>();

        foreach (var quote in quotes)
        {
            await windowStore.AppendAsync(
                quote.Ticker.Value, quote.Price, quote.ObservedAt, options.Retention, ct);

            await NotifyAsync(observer, quote.Ticker.Value, ct);
        }
    }

    /// <summary>The observer's contract says one failure must not stop the next ticker; a comment cannot.</summary>
    private async Task NotifyAsync(IPriceSampleObserver observer, string ticker, CancellationToken ct)
    {
        try
        {
            await observer.OnSampleStoredAsync(ticker, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogObserverFailed(logger, ex, ticker);
        }
    }

    private static HashSet<Ticker> Canonicalise(IReadOnlyList<string> targets)
    {
        var tickers = new HashSet<Ticker>();

        foreach (var candidate in targets)
        {
            if (Ticker.Create(candidate).TryPickT0(out var ticker, out _))
            {
                tickers.Add(ticker);
            }
        }

        return tickers;
    }

    [LoggerMessage(
        EventId = 5142,
        Level = LogLevel.Warning,
        Message = "A poll cycle failed; the host stays up and the next cycle runs on schedule")]
    private static partial void LogCycleFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 5143,
        Level = LogLevel.Warning,
        Message = "Evaluation of the fresh sample for {Ticker} failed; the rest of the cycle continues")]
    private static partial void LogObserverFailed(ILogger logger, Exception exception, string ticker);
}
