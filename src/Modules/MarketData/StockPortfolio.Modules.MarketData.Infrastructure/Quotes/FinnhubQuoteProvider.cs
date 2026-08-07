using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

internal sealed partial class FinnhubQuoteProvider(
    HttpClient client,
    IHttpClientFactory httpClientFactory,
    TimeProvider clock,
    ProviderKeyRejection rejection,
    ILogger<FinnhubQuoteProvider> logger) : IQuoteProvider
{
    private const int MaxDegreeOfParallelism = 4;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal const string ByokClientName = "FinnhubByok";

    public string Name => "Finnhub";

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(
        IReadOnlySet<Ticker> tickers, string? apiKeyOverride, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tickers);

        var quotes = new ConcurrentBag<Quote>();
        var failed = new ConcurrentBag<string>();

        // The per-item catch is load-bearing: without it one dead ticker cancels the rest and blanks the table.
        await Parallel.ForEachAsync(
            tickers,
            new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism, CancellationToken = ct },
            async (ticker, token) =>
            {
                try
                {
                    if (await FetchOneAsync(ticker, apiKeyOverride, token) is { } quote)
                    {
                        quotes.Add(quote);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed.Add(ticker.Value);

                    LogQuoteFailed(logger, ex, ticker.Value);
                }
            });

        // One line for the whole fetch: a 429 fails every symbol at once, and twenty identical warnings say nothing extra.
        if (!failed.IsEmpty)
        {
            LogFetchIncomplete(logger, failed.Count, tickers.Count, string.Join(", ", failed.Order(StringComparer.Ordinal)));
        }

        return [.. quotes];
    }

    public async Task<bool> SymbolExistsAsync(Ticker ticker, CancellationToken ct)
    {
        try
        {
            return await SearchAsync(ticker.Value, ct) is not { } matches || matches.Contains(ticker.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSymbolCheckFailed(logger, ex, ticker.Value);
            return true;
        }
    }

    public async Task<IReadOnlyList<SymbolMatch>> SearchSymbolsAsync(string query, CancellationToken ct)
    {
        try
        {
            return await SearchAsync(query, ct) is { } response ? response.Suggestions() : [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSearchFailed(logger, ex, query);
            return [];
        }
    }

    /// <summary>Checks a CANDIDATE key. Unlike every method above this does not fail open: unanswerable is never Accepted.</summary>
    public async Task<KeyVerdict> VerifyKeyAsync(string apiKey, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("search?q=AAPL", UriKind.Relative));

            request.Headers.Add("X-Finnhub-Token", apiKey);

            using var response = await httpClientFactory.CreateClient(ByokClientName).SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                return KeyVerdict.Accepted;
            }

            if (IsUnauthorised(response.StatusCode))
            {
                return KeyVerdict.Rejected;
            }

            LogVerifyUnexpectedStatus(logger, (int)response.StatusCode);
            return KeyVerdict.Unknown;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogVerifyFailed(logger, ex);
            return KeyVerdict.Unknown;
        }
    }

    internal static bool IsUnauthorised(HttpStatusCode status) =>
        status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    private async Task<Quote?> FetchOneAsync(Ticker ticker, string? apiKeyOverride, CancellationToken ct)
    {
        return await GetQuoteAsync(ticker, apiKeyOverride, ct) is { Price: { } price }
            ? new Quote(ticker, price, clock.GetUtcNow())
            : null;
    }

    private async Task<FinnhubQuoteResponse?> GetQuoteAsync(Ticker ticker, string? apiKeyOverride, CancellationToken ct)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $"quote?symbol={Uri.EscapeDataString(ticker.Value)}");

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));

        if (apiKeyOverride is not null)
        {
            request.Headers.Add("X-Finnhub-Token", apiKeyOverride);
        }

        var httpClient = apiKeyOverride is null ? client : httpClientFactory.CreateClient(ByokClientName);

        using var response = await httpClient.SendAsync(request, ct);

        if (IsUnauthorised(response.StatusCode))
        {
            NoteRejection(response.StatusCode, ticker.Value, isApplicationKey: apiKeyOverride is null);
            return null;
        }

        response.EnsureSuccessStatusCode();

        if (apiKeyOverride is null)
        {
            rejection.Clear();
        }

        return await ReadJsonAsync<FinnhubQuoteResponse>(response, ticker.Value, ct);
    }

    private async Task<FinnhubSearchResponse?> SearchAsync(string query, CancellationToken ct)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $"search?q={Uri.EscapeDataString(query)}");

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), ct);

        if (IsUnauthorised(response.StatusCode))
        {
            NoteRejection(response.StatusCode, query, isApplicationKey: true);
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await ReadJsonAsync<FinnhubSearchResponse>(response, query, ct);
    }

    /// <summary>Only the application's own key raises the flag; a user's rejected key is that one user's problem.</summary>
    private void NoteRejection(HttpStatusCode status, string subject, bool isApplicationKey)
    {
        LogAuthRejected(logger, (int)status, subject);

        if (isApplicationKey)
        {
            rejection.Raise();
        }
    }

    private async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, string subject, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException)
        {
            // Debug only: a body can be large and can echo the key back inside a URL.
            LogUnexpectedBody(logger, subject, body);

            throw;
        }
    }

    [LoggerMessage(
        EventId = 5100,
        Level = LogLevel.Debug,
        Message = "Finnhub quote failed for {Ticker}; this symbol falls back to its last known price")]
    private static partial void LogQuoteFailed(ILogger logger, Exception exception, string ticker);

    [LoggerMessage(
        EventId = 5107,
        Level = LogLevel.Warning,
        Message = "Finnhub could not quote {FailedCount} of {RequestedCount} symbols ({Tickers}); each falls back to its last known price")]
    private static partial void LogFetchIncomplete(
        ILogger logger, int failedCount, int requestedCount, string tickers);

    [LoggerMessage(
        EventId = 5108,
        Level = LogLevel.Debug,
        Message = "Finnhub returned a body that is not the expected shape for {Subject}: {Body}")]
    private static partial void LogUnexpectedBody(ILogger logger, string subject, string body);

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

    [LoggerMessage(
        EventId = 5105,
        Level = LogLevel.Warning,
        Message = "Finnhub key verification failed transport-side; treated as unanswerable, never as rejected")]
    private static partial void LogVerifyFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 5106,
        Level = LogLevel.Warning,
        Message = "Finnhub key verification got unexpected status {StatusCode}; treated as unanswerable")]
    private static partial void LogVerifyUnexpectedStatus(ILogger logger, int statusCode);
}
