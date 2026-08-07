namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

public interface IQuoteNudge
{
    void Nudge(string ticker, decimal percent, TimeSpan duration);
}
