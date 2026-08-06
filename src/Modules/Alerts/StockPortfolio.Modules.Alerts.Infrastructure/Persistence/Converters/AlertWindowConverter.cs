using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence.Converters;

/// <summary>Maps AlertWindow to the integer minutes the database stores; the cap is configuration, not a column.</summary>
internal sealed class AlertWindowConverter : ValueConverter<AlertWindow, int>
{
    public AlertWindowConverter()
        : base(window => window.Minutes, value => new AlertWindow(value))
    {
    }
}
