using Microsoft.Extensions.Logging;

using StackExchange.Redis;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Names;

internal sealed partial class RedisCompanyNameStore(
    IConnectionMultiplexer multiplexer,
    ILogger<RedisCompanyNameStore> logger) : ICompanyNameStore
{
    private const string KeyPrefix = "marketdata:name:";

    private const int MaxNameLength = 120;

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public async Task<IReadOnlyDictionary<Ticker, string>> ReadAsync(
        IReadOnlyCollection<Ticker> tickers,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tickers);

        var names = new Dictionary<Ticker, string>();

        if (tickers.Count == 0)
        {
            return names;
        }

        var ordered = tickers.ToArray();

        try
        {
            var values = await multiplexer.GetDatabase()
                .StringGetAsync([.. ordered.Select(ticker => (RedisKey)(KeyPrefix + ticker.Value))]);

            for (var index = 0; index < ordered.Length; index++)
            {
                if (TryDecode(values[index], out var name))
                {
                    names[ordered[index]] = name;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogReadFailed(logger, ex, ordered.Length);
        }

        return names;
    }

    public async Task WriteAsync(IReadOnlyCollection<SymbolMatch> matches, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(matches);

        if (matches.Count == 0)
        {
            return;
        }

        try
        {
            var database = multiplexer.GetDatabase();
            var writes = new List<Task>(matches.Count);

            foreach (var match in matches)
            {
                if (Encode(match.Name) is { } name)
                {
                    writes.Add(database.StringSetAsync(KeyPrefix + match.Ticker.Value, name, Lifetime));
                }
            }

            await Task.WhenAll(writes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWriteFailed(logger, ex, matches.Count);
        }
    }

    internal static string? Encode(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();

        return trimmed.Length <= MaxNameLength ? trimmed : trimmed[..MaxNameLength];
    }

    internal static bool TryDecode(string? stored, out string name)
    {
        name = string.Empty;

        if (Encode(stored) is not { } decoded)
        {
            return false;
        }

        name = decoded;

        return true;
    }

    [LoggerMessage(
        EventId = 5120,
        Level = LogLevel.Warning,
        Message = "Redis read of {Count} company names failed; those rows render with their ticker alone")]
    private static partial void LogReadFailed(ILogger logger, Exception exception, int count);

    [LoggerMessage(
        EventId = 5121,
        Level = LogLevel.Warning,
        Message = "Redis write of {Count} company names failed; the search itself is unaffected")]
    private static partial void LogWriteFailed(ILogger logger, Exception exception, int count);
}
