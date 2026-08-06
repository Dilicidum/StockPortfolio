using System.Globalization;
using OneOf;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Alerts.Domain;

/// <summary>How far a price must move before a threshold fires: above zero, at most a hundred percent.</summary>
public readonly record struct ThresholdPercent(decimal Value)
{
    /// <summary>The largest move worth asking about: a total loss is one hundred percent.</summary>
    public const decimal Maximum = 100m;

    /// <summary>Decimal places every stored threshold is rounded to, matching numeric(5,2).</summary>
    private const int StoredScale = 2;

    /// <summary>Creates a threshold, rounding to the stored scale before it judges the number.</summary>
    public static OneOf<ThresholdPercent, InvalidInput> Create(decimal raw)
    {
        // Rounded first, so the rule judges the number the column will actually hold: 0.001 stores as
        // zero, and a threshold of zero fires on every cycle forever.
        var stored = Math.Round(raw, StoredScale, MidpointRounding.ToEven);

        if (stored <= 0m || stored > Maximum)
        {
            return new InvalidInput(
                "thresholdPercent",
                $"A threshold is above 0 and at most {Maximum.ToString("0.##", CultureInfo.InvariantCulture)} "
                + "percent, to two decimal places.");
        }

        return new ThresholdPercent(stored);
    }

    /// <summary>Returns the percentage as a plain number, so a log line reads as one.</summary>
    public override string ToString() => Value.ToString("0.##", CultureInfo.InvariantCulture);
}
