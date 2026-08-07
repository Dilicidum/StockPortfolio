using System.Globalization;

namespace StockPortfolio.Modules.Alerts.Domain;

public readonly record struct FiredAlertId(Guid Value)
{
    public static FiredAlertId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
