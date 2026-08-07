using StockPortfolio.Modules.MarketData.Contracts;

namespace StockPortfolio.Tests.Fakes;

internal sealed class FakePriceWindowReader : IPriceWindowReader
{
    private readonly Dictionary<int, PriceWindow> _byMinutes = [];

    public List<TimeSpan> Requested { get; } = [];

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
