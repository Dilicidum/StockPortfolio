using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

internal sealed class ScriptedQuoteProvider : IQuoteProvider
{
    private readonly Dictionary<string, decimal> _prices;

    private readonly KeyVerdict _verifyKeyVerdict;

    private ScriptedQuoteProvider(Dictionary<string, decimal> prices, KeyVerdict verifyKeyVerdict = KeyVerdict.Accepted)
    {
        _prices = prices;
        _verifyKeyVerdict = verifyKeyVerdict;
    }

    public static ScriptedQuoteProvider ServingNothing { get; } = new([]);

    public string Name => "Scripted";

    // The BYOK dashboard test's only way to prove which key a fetch actually used.
    public List<string?> ApiKeyOverridesSeen { get; } = [];

    public static ScriptedQuoteProvider Serving(params (string Ticker, decimal Price)[] prices)
    {
        ArgumentNullException.ThrowIfNull(prices);

        return new ScriptedQuoteProvider(
            prices.ToDictionary(entry => entry.Ticker, entry => entry.Price, StringComparer.Ordinal));
    }

    public static ScriptedQuoteProvider VerifyingKeyAs(KeyVerdict verdict) => new([], verdict);

    // An unscripted symbol is absent, never zero-valued — which is what the real per-item catch does.
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

    // True even when this provider cannot price the symbol: the existence check fails open.
    public Task<bool> SymbolExistsAsync(Ticker ticker, CancellationToken ct) => Task.FromResult(true);

    public Task<IReadOnlyList<SymbolMatch>> SearchSymbolsAsync(string query, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SymbolMatch>>([]);

    public Task<KeyVerdict> VerifyKeyAsync(string apiKey, CancellationToken ct) => Task.FromResult(_verifyKeyVerdict);
}
