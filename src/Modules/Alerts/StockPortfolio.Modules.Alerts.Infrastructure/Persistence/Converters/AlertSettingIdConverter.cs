using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence.Converters;

/// <summary>Maps the strongly-typed AlertSettingId to the plain Guid the database stores.</summary>
internal sealed class AlertSettingIdConverter : ValueConverter<AlertSettingId, Guid>
{
    public AlertSettingIdConverter()
        : base(id => id.Value, value => new AlertSettingId(value))
    {
    }
}
