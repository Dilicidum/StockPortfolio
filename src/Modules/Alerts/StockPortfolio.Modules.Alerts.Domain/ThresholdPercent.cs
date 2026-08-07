using System.Globalization;
using OneOf;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Alerts.Domain;

public readonly record struct ThresholdPercent(decimal Value)
{
    public const decimal Maximum = 100m;

    private const int StoredScale = 2;

    public static OneOf<ThresholdPercent, InvalidInput> Create(decimal raw)
    {
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

    public override string ToString() => Value.ToString("0.##", CultureInfo.InvariantCulture);
}
