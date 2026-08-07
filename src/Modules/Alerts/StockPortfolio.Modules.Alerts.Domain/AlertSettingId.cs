using System.Globalization;

namespace StockPortfolio.Modules.Alerts.Domain;

public readonly record struct AlertSettingId(Guid Value)
{
    public static AlertSettingId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
