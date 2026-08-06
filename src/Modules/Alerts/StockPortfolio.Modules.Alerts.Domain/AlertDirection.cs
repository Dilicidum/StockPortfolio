namespace StockPortfolio.Modules.Alerts.Domain;

/// <summary>Which way a price has to move for a threshold to fire.</summary>
public enum AlertDirection
{
    /// <summary>The price fell by at least the threshold.</summary>
    Fall = 0,

    /// <summary>The price rose by at least the threshold.</summary>
    Rise = 1,
}
