using StockPortfolio.Modules.Alerts.Application.Streaming;

namespace StockPortfolio.Modules.Alerts.Application.Abstractions;

public interface IAlertPublisher
{
    Task PublishAsync(AlertNotification notification, CancellationToken ct);
}
