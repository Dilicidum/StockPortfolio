using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence.Converters;

internal sealed class TickerConverter : ValueConverter<Ticker, string>
{
    public TickerConverter()
        : base(ticker => ticker.Value, value => new Ticker(value))
    {
    }
}
