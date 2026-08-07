using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence.Converters;

internal sealed class TickerConverter : ValueConverter<Ticker, string>
{
    public TickerConverter()
        : base(ticker => ticker.Value, value => new Ticker(value))
    {
    }
}
