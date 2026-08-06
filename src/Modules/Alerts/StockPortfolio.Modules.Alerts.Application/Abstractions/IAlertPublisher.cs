using StockPortfolio.Modules.Alerts.Application.Streaming;

namespace StockPortfolio.Modules.Alerts.Application.Abstractions;

/// <summary>Pushes one alert to whichever replica is holding that user's stream, if any is.</summary>
public interface IAlertPublisher
{
    /// <summary>Publishes to the user's channel. The row is already saved, so a failure here loses nothing.</summary>
    Task PublishAsync(AlertNotification notification, CancellationToken ct);
}
