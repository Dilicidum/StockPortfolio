using System.Globalization;
using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Modules.MarketData.Contracts;

namespace StockPortfolio.Modules.Alerts.Application.Evaluation;

public static class MoveAssessment
{
    public static MoveVerdict Assess(PriceWindow window, decimal thresholdPercent)
    {
        ArgumentNullException.ThrowIfNull(window);

        var endpoint = PercentChange(window.Oldest, window.Current);
        var fall = PercentChange(window.High, window.Current);
        var rise = PercentChange(window.Low, window.Current);

        var falling = Math.Abs(fall) >= Math.Abs(rise);
        var extreme = falling ? fall : rise;
        var direction = falling ? AlertDirection.Fall : AlertDirection.Rise;
        var reference = falling ? window.High : window.Low;

        // Sign agreement: without it a price oscillating in a band wider than the threshold fires against the opposite extreme every cycle, forever.
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

    public static string Describe(AlertDirection direction, decimal extreme)
    {
        var size = Math.Abs(extreme).ToString("0.##", CultureInfo.InvariantCulture);

        return direction == AlertDirection.Fall
            ? "fell " + size + "% from the window high"
            : "rose " + size + "% from the window low";
    }
}
