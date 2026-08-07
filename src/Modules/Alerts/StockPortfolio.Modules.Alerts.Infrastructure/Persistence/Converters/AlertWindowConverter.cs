using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence.Converters;

internal sealed class AlertWindowConverter : ValueConverter<AlertWindow, int>
{
    public AlertWindowConverter()
        : base(window => window.Minutes, value => new AlertWindow(value))
    {
    }
}
