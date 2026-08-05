namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

/// <summary>The dev hook's seam: shift a generated price for a while, to drive a demo.</summary>
public interface IQuoteNudge
{
    /// <summary>Applies a percentage shift to one ticker's generated price for the given duration.</summary>
    void Nudge(string ticker, decimal percent, TimeSpan duration);
}
