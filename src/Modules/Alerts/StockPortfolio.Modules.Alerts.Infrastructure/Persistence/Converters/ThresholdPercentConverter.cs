using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence.Converters;

/// <summary>Maps ThresholdPercent to the numeric(5,2) the database stores; Create already rounded it.</summary>
internal sealed class ThresholdPercentConverter : ValueConverter<ThresholdPercent, decimal>
{
    public ThresholdPercentConverter()
        : base(percent => percent.Value, value => new ThresholdPercent(value))
    {
    }
}
