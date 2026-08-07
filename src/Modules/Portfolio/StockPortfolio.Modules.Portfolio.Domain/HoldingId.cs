using System.Globalization;

namespace StockPortfolio.Modules.Portfolio.Domain;

public readonly record struct HoldingId(Guid Value)
{
    public static HoldingId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
