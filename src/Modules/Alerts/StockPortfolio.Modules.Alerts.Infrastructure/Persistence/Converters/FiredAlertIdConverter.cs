using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence.Converters;

internal sealed class FiredAlertIdConverter : ValueConverter<FiredAlertId, Guid>
{
    public FiredAlertIdConverter()
        : base(id => id.Value, value => new FiredAlertId(value))
    {
    }
}
