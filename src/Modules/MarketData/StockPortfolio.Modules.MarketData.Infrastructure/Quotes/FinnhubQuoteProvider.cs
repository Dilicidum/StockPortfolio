using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;

using Microsoft.Extensions.Logging;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

/// <summary>The live provider. Fetches and returns; recording what was fetched belongs to QuoteReader.</summary>
internal sealed partial class FinnhubQuoteProvider(
    HttpClient client,
    TimeProvider clock,
    ILogger<FinnhubQuoteProvider> logger) : IQuoteProvider
{
    /// <summary>Four in flight, to bound sockets rather than to pace requests — that is Finnhub's job now.</summary>
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
                    if (await FetchOneAsync(ticker, token) is { } quote)
                    {
                        quotes.Add(quote);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogQuoteFailed(logger, ex, ticker.Value);
                }
            });

        return [.. quotes];
    }

    /// <summary>Existence by exact symbol match on /search, never off a quote body — see the comment inside.</summary>
    public async Task<bool> SymbolExistsAsync(Ticker ticker, CancellationToken ct)
    {
        try
        {
            // /quote answers c:0 for a symbol that does not exist AND for a healthy one Finnhub blipped
            // on, so it cannot tell the two apart and would answer "known" to everything. /search can,
            // and null still means the provider could not answer, so an outage never rejects a purchase.
            return await SearchAsync(ticker.Value, ct) is not { } matches || matches.Contains(ticker.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSymbolCheckFailed(logger, ex, ticker.Value);
            return true;
        }
    }

    /// <summary>The same /search call the existence check makes, keeping the names instead of discarding them.</summary>
    public async Task<IReadOnlyList<SymbolMatch>> SearchSymbolsAsync(string query, CancellationToken ct)
    {
        try
        {
            // Empty rather than a throw on every failure below: search is a convenience over a field that
            // still accepts a typed symbol, so an outage must degrade to no suggestions and nothing else.
            return await SearchAsync(query, ct) is { } response ? response.Suggestions() : [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSearchFailed(logger, ex, query);
            return [];
        }
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

    private async Task<FinnhubSearchResponse?> SearchAsync(string query, CancellationToken ct)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $"search?q={Uri.EscapeDataString(query)}");

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), ct);

        if (IsUnauthorised(response.StatusCode))
        {
            LogAuthRejected(logger, (int)response.StatusCode, query);
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
        EventId = 5102,
        Level = LogLevel.Error,
        Message = "Finnhub rejected the API key with {StatusCode} while fetching {Ticker}; not retried")]
    private static partial void LogAuthRejected(ILogger logger, int statusCode, string ticker);

    [LoggerMessage(
        EventId = 5103,
        Level = LogLevel.Warning,
        Message = "Finnhub symbol check failed for {Ticker}; treating it as known so the add still succeeds")]
    private static partial void LogSymbolCheckFailed(ILogger logger, Exception exception, string ticker);

    [LoggerMessage(
        EventId = 5104,
        Level = LogLevel.Warning,
        Message = "Finnhub search failed for '{Query}'; the field falls back to being a plain text box")]
    private static partial void LogSearchFailed(ILogger logger, Exception exception, string query);
}
