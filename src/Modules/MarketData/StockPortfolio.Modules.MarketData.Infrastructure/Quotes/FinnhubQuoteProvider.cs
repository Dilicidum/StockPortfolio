using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Threading.RateLimiting;

using Microsoft.Extensions.Logging;

using Polly.CircuitBreaker;
using Polly.Timeout;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

/// <summary>The live provider. Fetches and returns; recording what was fetched belongs to QuoteReader.</summary>
internal sealed partial class FinnhubQuoteProvider(
    HttpClient client,
    RateLimiter budget,
    TimeProvider clock,
    ILogger<FinnhubQuoteProvider> logger) : IQuoteProvider
{
    /// <summary>Four in flight: the shared bucket serialises the excess anyway, so more only holds sockets.</summary>
    private const int MaxDegreeOfParallelism = 4;

    public string Name => "Finnhub";

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlySet<Ticker> tickers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tickers);

        var quotes = new ConcurrentBag<Quote>();

        // The per-item catch is load-bearing: without it one dead ticker cancels the rest and blanks the table.
        await Parallel.ForEachAsync(
            tickers,
            new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism, CancellationToken = ct },
            async (ticker, token) =>
            {
                try
                {
                    using var lease = await budget.AcquireAsync(1, token);

                    if (!lease.IsAcquired)
                    {
                        LogBudgetExhausted(logger, ticker.Value);
                        return;
                    }

                    if (await FetchOneAsync(ticker, token) is { } quote)
                    {
                        quotes.Add(quote);
                    }
                }
                catch (HttpRequestException ex) { LogQuoteFailed(logger, ex, ticker.Value); }
                catch (TimeoutRejectedException ex) { LogQuoteFailed(logger, ex, ticker.Value); }
                catch (BrokenCircuitException ex) { LogQuoteFailed(logger, ex, ticker.Value); }
            });

        return [.. quotes];
    }

    /// <summary>Existence by exact symbol match on /search, never off a quote body — see the comment inside.</summary>
    public async Task<bool> SymbolExistsAsync(Ticker ticker, CancellationToken ct)
    {
        try
        {
            using var lease = await budget.AcquireAsync(1, ct);

            if (!lease.IsAcquired)
            {
                return true;
            }

            // /quote answers c:0 for a symbol that does not exist AND for a healthy one Finnhub blipped
            // on, so it cannot tell the two apart and would answer "known" to everything. /search can,
            // and null still means the provider could not answer, so an outage never rejects a purchase.
            return await SearchAsync(ticker, ct) is not { } matches || matches.Contains(ticker.Value);
        }
        catch (HttpRequestException ex) { LogSymbolCheckFailed(logger, ex, ticker.Value); return true; }
        catch (TimeoutRejectedException ex) { LogSymbolCheckFailed(logger, ex, ticker.Value); return true; }
        catch (BrokenCircuitException ex) { LogSymbolCheckFailed(logger, ex, ticker.Value); return true; }
    }

    /// <summary>401 and 403 carry the same body and mean the same thing here; neither is ever retried.</summary>
    internal static bool IsUnauthorised(HttpStatusCode status) =>
        status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    private async Task<Quote?> FetchOneAsync(Ticker ticker, CancellationToken ct)
    {
        // ObservedAt is when this app fetched, never Finnhub's t: t freezes at Friday's close, which would
        // render every weekend dashboard amber while the provider is perfectly healthy.
        return await GetQuoteAsync(ticker, ct) is { Price: { } price }
            ? new Quote(ticker, price, clock.GetUtcNow())
            : null;
    }

    private async Task<FinnhubQuoteResponse?> GetQuoteAsync(Ticker ticker, CancellationToken ct)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $"quote?symbol={Uri.EscapeDataString(ticker.Value)}");

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), ct);

        if (IsUnauthorised(response.StatusCode))
        {
            LogAuthRejected(logger, (int)response.StatusCode, ticker.Value);
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<FinnhubQuoteResponse>(ct);
    }

    private async Task<FinnhubSearchResponse?> SearchAsync(Ticker ticker, CancellationToken ct)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $"search?q={Uri.EscapeDataString(ticker.Value)}");

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), ct);

        if (IsUnauthorised(response.StatusCode))
        {
            LogAuthRejected(logger, (int)response.StatusCode, ticker.Value);
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<FinnhubSearchResponse>(ct);
    }

    [LoggerMessage(
        EventId = 5100,
        Level = LogLevel.Warning,
        Message = "Finnhub quote failed for {Ticker}; this symbol falls back to its last known price")]
    private static partial void LogQuoteFailed(ILogger logger, Exception exception, string ticker);

    [LoggerMessage(
        EventId = 5101,
        Level = LogLevel.Warning,
        Message = "Finnhub call budget exhausted before {Ticker} could be fetched")]
    private static partial void LogBudgetExhausted(ILogger logger, string ticker);

    [LoggerMessage(
        EventId = 5102,
        Level = LogLevel.Error,
        Message = "Finnhub rejected the API key with {StatusCode} while fetching {Ticker}; not retried")]
    private static partial void LogAuthRejected(ILogger logger, int statusCode, string ticker);

    [LoggerMessage(
        EventId = 5103,
        Level = LogLevel.Warning,
        Message = "Finnhub symbol check failed for {Ticker}; treating it as known so the add still succeeds")]
    private static partial void LogSymbolCheckFailed(ILogger logger, Exception exception, string ticker);
}
