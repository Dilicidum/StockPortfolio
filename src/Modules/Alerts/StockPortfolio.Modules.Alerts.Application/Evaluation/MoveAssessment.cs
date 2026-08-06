using StockPortfolio.Modules.MarketData.Contracts;

namespace StockPortfolio.Modules.Alerts.Application.Evaluation;

/// <summary>The false-positive rule, pure: no I/O, no clock, so the whole of it is unit-testable.</summary>
public static class MoveAssessment
{
    /// <summary>Applies the sign-agreement rule to one window against one threshold.</summary>
    public static MoveVerdict Assess(PriceWindow window, decimal thresholdPercent)
    {
        // TODO(you): when both moves clear the threshold the same way, report the extreme or the endpoint? §2.2 says extreme.
        // TODO(you): word Reason for whoever reads it at 7am — e.g. "fell 5.33% from the window high".
        throw new NotImplementedException(
            "The sign-agreement rule is the user's to write. See docs/plan/phase-4-implementation.md §2.2.");
    }
}
