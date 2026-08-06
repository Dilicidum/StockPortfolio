using System.Globalization;
using OneOf;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Alerts.Domain;

/// <summary>How far back a threshold looks. The cap is configuration, so it arrives as an argument.</summary>
public readonly record struct AlertWindow(int Minutes)
{
    /// <summary>The shortest window that means anything: one poll interval.</summary>
    public const int MinimumMinutes = 1;

    /// <summary>Gets the window as a span, which is what a price window read is asked for.</summary>
    public TimeSpan Duration => TimeSpan.FromMinutes(Minutes);

    /// <summary>Creates a window, rejecting anything outside one minute to the configured cap.</summary>
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

    /// <summary>Returns the minutes, so a log line reads as a number rather than a struct name.</summary>
    public override string ToString() => Minutes.ToString(CultureInfo.InvariantCulture);
}
