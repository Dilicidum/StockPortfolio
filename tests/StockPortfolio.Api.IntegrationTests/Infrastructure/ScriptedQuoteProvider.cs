using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

/// <summary>A provider that answers for the symbols it was scripted with and silently omits the rest.</summary>
internal sealed class ScriptedQuoteProvider : IQuoteProvider
{
    private readonly Dictionary<string, decimal> _prices;

    private readonly KeyVerdict _verifyKeyVerdict;

    private ScriptedQuoteProvider(Dictionary<string, decimal> prices, KeyVerdict verifyKeyVerdict = KeyVerdict.Accepted)
    {
        _prices = prices;
        _verifyKeyVerdict = verifyKeyVerdict;
    }

    /// <summary>A provider that can price nothing at all — the whole-provider-down row of §2.5.</summary>
    public static ScriptedQuoteProvider ServingNothing { get; } = new([]);

    /// <summary>Named so a failing assertion says which host it came from.</summary>
    public string Name => "Scripted";

    /// <summary>Every apiKeyOverride GetQuotesAsync was called with, in call order — the BYOK dashboard
    /// test's only way to prove which key a fetch actually used.</summary>
    public List<string?> ApiKeyOverridesSeen { get; } = [];

    /// <summary>A provider that answers for exactly these symbols and fails every other one.</summary>
    public static ScriptedQuoteProvider Serving(params (string Ticker, decimal Price)[] prices)
    {
        ArgumentNullException.ThrowIfNull(prices);

        return new ScriptedQuoteProvider(
            prices.ToDictionary(entry => entry.Ticker, entry => entry.Price, StringComparer.Ordinal));
    }

    /// <summary>A provider that answers every VerifyKeyAsync call with the same scripted verdict, and
    /// prices nothing — the BYOK tests care about the verdict, never about a quote.</summary>
    public static ScriptedQuoteProvider VerifyingKeyAs(KeyVerdict verdict) => new([], verdict);

    /// <summary>An unscripted symbol is absent, never zero-valued — which is what the real per-item catch does.</summary>
    public Task<IReadOnlyList<Quote>> GetQuotesAsync(
        IReadOnlySet<Ticker> tickers, string? apiKeyOverride, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tickers);

        ApiKeyOverridesSeen.Add(apiKeyOverride);

        var now = TimeProvider.System.GetUtcNow();

        return Task.FromResult<IReadOnlyList<Quote>>(
        [
            .. tickers
                .Where(ticker => _prices.ContainsKey(ticker.Value))
                .Select(ticker => new Quote(ticker, _prices[ticker.Value], now)),
        ]);
    }

    /// <summary>True even when this provider cannot price the symbol: existence fails open, per §2.11.</summary>
    public Task<bool> SymbolExistsAsync(Ticker ticker, CancellationToken ct) => Task.FromResult(true);

    /// <summary>Nothing, always — which is exactly what a provider that cannot answer a search returns.</summary>
    public Task<IReadOnlyList<SymbolMatch>> SearchSymbolsAsync(string query, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SymbolMatch>>([]);

    /// <summary>The verdict this instance was built with — VerifyingKeyAs, or Accepted by default.</summary>
    public Task<KeyVerdict> VerifyKeyAsync(string apiKey, CancellationToken ct) => Task.FromResult(_verifyKeyVerdict);
}
