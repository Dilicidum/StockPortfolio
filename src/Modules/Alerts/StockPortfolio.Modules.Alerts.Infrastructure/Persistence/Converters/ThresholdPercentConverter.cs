using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence.Converters;

internal sealed class ThresholdPercentConverter : ValueConverter<ThresholdPercent, decimal>
{
    public ThresholdPercentConverter()
        : base(percent => percent.Value, value => new ThresholdPercent(value))
    {
    }
}
