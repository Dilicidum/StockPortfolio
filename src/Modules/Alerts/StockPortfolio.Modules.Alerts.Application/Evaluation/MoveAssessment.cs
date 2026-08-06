using System.Globalization;
using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Modules.MarketData.Contracts;

namespace StockPortfolio.Modules.Alerts.Application.Evaluation;

/// <summary>The false-positive rule, pure: no I/O, no clock, so the whole of it is unit-testable.</summary>
public static class MoveAssessment
{
    /// <summary>Applies the sign-agreement rule to one window against one threshold.</summary>
    public static MoveVerdict Assess(PriceWindow window, decimal thresholdPercent)
    {
        ArgumentNullException.ThrowIfNull(window);

        var endpoint = PercentChange(window.Oldest, window.Current);
        var fall = PercentChange(window.High, window.Current);
        var rise = PercentChange(window.Low, window.Current);

        // The bigger of the two extremes is the candidate; the endpoint move only decides whether to trust it.
        var falling = Math.Abs(fall) >= Math.Abs(rise);
        var extreme = falling ? fall : rise;
        var direction = falling ? AlertDirection.Fall : AlertDirection.Rise;
        var reference = falling ? window.High : window.Low;

        // Sign agreement. Without it, a price oscillating inside a band wider than the threshold fires
        // against the opposite extreme every single cycle, forever, held back only by the cooldown.
        var agree = Math.Sign(endpoint) == Math.Sign(extreme);

        return new MoveVerdict(
            agree && Math.Abs(extreme) >= thresholdPercent,
            direction,
            extreme,
            endpoint,
            reference,
            Describe(direction, extreme));
    }

    private static decimal PercentChange(decimal from, decimal to) =>
        from == 0m ? 0m : (to - from) / from * 100m;

    /// <summary>Names the comparison, because an alert that does not say what it measured gets turned off.</summary>
    public static string Describe(AlertDirection direction, decimal extreme)
    {
        var size = Math.Abs(extreme).ToString("0.##", CultureInfo.InvariantCulture);

        return direction == AlertDirection.Fall
            ? "fell " + size + "% from the window high"
            : "rose " + size + "% from the window low";
    }
}
