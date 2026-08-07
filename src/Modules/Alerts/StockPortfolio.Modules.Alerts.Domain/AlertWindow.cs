using System.Globalization;
using OneOf;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Alerts.Domain;

public readonly record struct AlertWindow(int Minutes)
{
    public const int MinimumMinutes = 1;

    public TimeSpan Duration => TimeSpan.FromMinutes(Minutes);

    public static OneOf<AlertWindow, InvalidInput> Create(int minutes, int maxMinutes)
    {
        if (minutes < MinimumMinutes || minutes > maxMinutes)
        {
            return new InvalidInput(
                "windowMinutes",
                $"A window is {MinimumMinutes} to {maxMinutes.ToString(CultureInfo.InvariantCulture)} minutes. "
                + "A move measured over days is a trend, not a sharp move.");
        }

        return new AlertWindow(minutes);
    }

    public override string ToString() => Minutes.ToString(CultureInfo.InvariantCulture);
}
