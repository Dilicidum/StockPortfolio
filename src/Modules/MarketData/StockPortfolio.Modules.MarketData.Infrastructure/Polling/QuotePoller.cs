using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Polling;

internal sealed partial class QuotePoller(
    IServiceScopeFactory scopeFactory,
    IPollLease lease,
    IPriceWindowStore windowStore,
    ILastKnownPriceStore lastKnownStore,
    PollingOptions options,
    TimeProvider clock,
    ILogger<QuotePoller> logger) : BackgroundService
{
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
        }
    }

    private async Task RunCycleSafelyAsync(CancellationToken ct)
    {
        // The catch goes INSIDE the loop: an unhandled exception out of a BackgroundService takes the whole host down.
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
        await using var scope = scopeFactory.CreateAsyncScope();

        var targets = await scope.ServiceProvider
            .GetRequiredService<IPollTargetSource>()
            .GetPollTargetsAsync(ct);

        var tickers = Canonicalise(targets);

        if (tickers.Count == 0)
        {
            return;
        }

        var quotes = await scope.ServiceProvider
            .GetRequiredService<IQuoteProvider>()
            .GetQuotesAsync(tickers, apiKeyOverride: null, ct);

        if (quotes.Count == 0)
        {
            return;
        }

        await lastKnownStore.WriteAsync(quotes, ct);

        var observer = scope.ServiceProvider.GetRequiredService<IPriceSampleObserver>();

        foreach (var quote in quotes)
        {
            await windowStore.AppendAsync(
                quote.Ticker.Value, quote.Price, quote.ObservedAt, options.Retention, ct);

            await NotifyAsync(observer, quote.Ticker.Value, ct);
        }
    }

    /// <summary>The per-ticker catch, and the only thing enforcing that one failed observer does not end the cycle.</summary>
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
            if (Ticker.TryParse(candidate) is { } ticker)
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
