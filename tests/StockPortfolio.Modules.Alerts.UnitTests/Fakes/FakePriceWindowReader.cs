using StockPortfolio.Modules.MarketData.Contracts;

namespace StockPortfolio.Tests.Fakes;

/// <summary>MarketData's one read, stubbed — and it records what was asked for, not only what it answered.</summary>
internal sealed class FakePriceWindowReader : IPriceWindowReader
{
    private readonly Dictionary<int, PriceWindow> _byMinutes = [];

    /// <summary>Gets every window length asked for, in order, so widening one user's window is visible.</summary>
    public List<TimeSpan> Requested { get; } = [];

    /// <summary>Answers this window length with this series; anything else comes back absent.</summary>
    public FakePriceWindowReader Returning(int windowMinutes, PriceWindow window)
    {
        _byMinutes[windowMinutes] = window;

        return this;
    }

    public Task<PriceWindow?> GetWindowAsync(string ticker, TimeSpan window, CancellationToken ct)
    {
        Requested.Add(window);

        return Task.FromResult(_byMinutes.TryGetValue((int)window.TotalMinutes, out var found) ? found : null);
    }
}
