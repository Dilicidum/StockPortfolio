using System.Globalization;

namespace StockPortfolio.Modules.Alerts.Domain;

/// <summary>The identity of an AlertSetting.</summary>
public readonly record struct AlertSettingId(Guid Value)
{
    /// <summary>Creates a new, time-ordered setting id.</summary>
    public static AlertSettingId New() => new(Guid.CreateVersion7());

    /// <summary>Returns the canonical hyphenated form of the underlying value.</summary>
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
