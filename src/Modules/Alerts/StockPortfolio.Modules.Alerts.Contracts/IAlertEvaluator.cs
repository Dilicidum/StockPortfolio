namespace StockPortfolio.Modules.Alerts.Contracts;

/// <summary>Told that a ticker has a fresh sample. The inbound half of the poller's cycle.</summary>
public interface IAlertEvaluator
{
    /// <summary>Judges every enabled threshold on this ticker. Never throws: one ticker must not stop a cycle.</summary>
    Task EvaluateAsync(string ticker, CancellationToken ct);
}
