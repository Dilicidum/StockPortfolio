namespace StockPortfolio.Modules.Alerts.Contracts;

public interface IAlertEvaluator
{
    Task EvaluateAsync(string ticker, CancellationToken ct);
}
