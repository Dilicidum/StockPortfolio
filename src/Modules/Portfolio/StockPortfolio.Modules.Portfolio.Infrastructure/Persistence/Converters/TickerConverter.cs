using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence.Converters;

/// <summary>Maps Ticker to the plain string the database stores; the stored value is already canonical.</summary>
internal sealed class TickerConverter : ValueConverter<Ticker, string>
{
    // The read direction skips Create on purpose: the stored value was canonicalised on the way in,
    // and re-validating every row is the guard-in-the-constructor trap wearing a different hat.
    public TickerConverter()
        : base(ticker => ticker.Value, value => new Ticker(value))
    {
    }
}
