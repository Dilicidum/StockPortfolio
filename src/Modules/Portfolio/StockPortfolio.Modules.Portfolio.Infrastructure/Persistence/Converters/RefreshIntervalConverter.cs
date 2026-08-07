using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence.Converters;

internal sealed class RefreshIntervalConverter : ValueConverter<RefreshInterval, int>
{
    public RefreshIntervalConverter()
        : base(interval => interval.Seconds, value => new RefreshInterval(value))
    {
    }
}
