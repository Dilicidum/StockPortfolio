using System.Collections.Concurrent;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

/// <summary>Generated prices for the keyless path. Deterministic per ticker and minute, across processes.</summary>
internal sealed class FakeQuoteProvider(FakeQuoteOptions options, TimeProvider clock) : IQuoteProvider, IQuoteNudge
{
    private const uint Fnv1aOffsetBasis = 2166136261;
    private const uint Fnv1aPrime = 16777619;

    /// <summary>Base prices land in $20.00 to $499.99 and never move for a given symbol.</summary>
    private const uint BasePriceSpread = 48000;

    /// <summary>Every symbol here fits the ticker shape, so any of them can be picked and then added.</summary>
    private static readonly (string Symbol, string Name)[] Catalogue =
    [
        ("AAPL", "Apple Inc"),
        ("ADBE", "Adobe Inc"),
        ("AMD", "Advanced Micro Devices Inc"),
        ("AMZN", "Amazon.com Inc"),
        ("BA", "Boeing Company"),
        ("CRM", "Salesforce Inc"),
        ("DIS", "Walt Disney Company"),
        ("GOOGL", "Alphabet Inc"),
        ("IBM", "International Business Machines Corporation"),
        ("INTC", "Intel Corporation"),
        ("JPM", "JPMorgan Chase & Co"),
        ("KO", "Coca-Cola Company"),
        ("META", "Meta Platforms Inc"),
        ("MSFT", "Microsoft Corporation"),
        ("NFLX", "Netflix Inc"),
        ("NVDA", "NVIDIA Corporation"),
        ("ORCL", "Oracle Corporation"),
        ("PEP", "PepsiCo Inc"),
        ("TSLA", "Tesla Inc"),
        ("V", "Visa Inc"),
        ("WMT", "Walmart Inc"),
    ];

    private readonly ConcurrentDictionary<string, (decimal Percent, DateTimeOffset ExpiresAt)> nudges =
        new(StringComparer.Ordinal);

    public string Name => "Fake";

    public Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlySet<Ticker> tickers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tickers);

        var now = clock.GetUtcNow();

        return Task.FromResult<IReadOnlyList<Quote>>(
            [.. tickers.Select(ticker => new Quote(ticker, PriceAt(ticker, now), now))]);
    }

    /// <summary>Anything of the right shape exists, or every HoldingsTests case using an invented symbol dies.</summary>
    public Task<bool> SymbolExistsAsync(Ticker ticker, CancellationToken ct) =>
        Task.FromResult(Ticker.Create(ticker.Value).IsT0);

    /// <summary>A fixed catalogue, so `docker compose up` with no API key still gives working search.</summary>
    public Task<IReadOnlyList<SymbolMatch>> SearchSymbolsAsync(string query, CancellationToken ct)
    {
        var trimmed = (query ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<SymbolMatch>>([]);
        }

        // Prefix on the symbol, anywhere in the name: "appl" must find Apple, and "aap" must find AAPL.
        return Task.FromResult<IReadOnlyList<SymbolMatch>>(
        [
            .. Catalogue
                .Where(entry =>
                    entry.Symbol.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                .Select(entry => new SymbolMatch(Ticker.Create(entry.Symbol).AsT0, entry.Name)),
        ]);
    }

    public void Nudge(string ticker, decimal percent, TimeSpan duration)
    {
        var key = Ticker.Create(ticker).TryPickT0(out var parsed, out _) ? parsed.Value : null;

        if (key is not null)
        {
            nudges[key] = (percent, clock.GetUtcNow() + duration);
        }
    }

    /// <summary>FNV-1a, never string.GetHashCode: that is randomised per process, so two replicas would diverge.</summary>
    internal static uint Fnv1a(string value)
    {
        var hash = Fnv1aOffsetBasis;

        foreach (var character in value)
        {
            hash = (hash ^ character) * Fnv1aPrime;
        }

        return hash;
    }

    internal static uint Fnv1a(string value, int minute)
    {
        var hash = Fnv1a(value);

        for (var shift = 0; shift < 32; shift += 8)
        {
            hash = (hash ^ (byte)(minute >> shift)) * Fnv1aPrime;
        }

        return hash;
    }

    private decimal PriceAt(Ticker ticker, DateTimeOffset now)
    {
        var price = 20m + (Fnv1a(ticker.Value) % BasePriceSpread) / 100m;

        // The walk restarts at UTC midnight. Honest for a fake, and cheaper than carrying state.
        var minutes = (int)(now.UtcDateTime - now.UtcDateTime.Date).TotalMinutes;

        for (var minute = 1; minute <= minutes; minute++)
        {
            var draw = (decimal)Fnv1a(ticker.Value, minute) / uint.MaxValue;

            price *= 1m + options.DriftPerMinute + (((draw * 2m) - 1m) * options.VolatilityPerMinute);

            if (price < 0.01m)
            {
                price = 0.01m;
            }
        }

        return decimal.Round(price * (1m + (ActiveNudge(ticker.Value, now) / 100m)), 4);
    }

    private decimal ActiveNudge(string ticker, DateTimeOffset now) =>
        nudges.TryGetValue(ticker, out var nudge) && nudge.ExpiresAt > now ? nudge.Percent : 0m;
}
