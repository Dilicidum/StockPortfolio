using Microsoft.Extensions.Logging;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Application.Streaming;

/// <summary>Persist, then publish. One place, so a second caller cannot get the order the other way round.</summary>
public sealed partial class AlertDispatcher(
    IFiredAlertRepository firedAlerts,
    IAlertPublisher publisher,
    ILogger<AlertDispatcher> logger)
{
    /// <summary>Saves the alert, then pushes it. A failed push is logged and never rethrown.</summary>
    public async Task DispatchAsync(FiredAlert alert, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(alert);

        await firedAlerts.AddAsync(alert, ct);

        try
        {
            await publisher.PublishAsync(AlertNotification.From(alert), ct);
        }

        // Deliberately every exception, not RedisException alone: the row is saved, the history read
        // will carry it, and there is nothing a publisher can throw that is worth losing an alert over.
        // Rethrowing would also take down the poll cycle and every later ticker in it.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPublishFailed(logger, ex, alert.Id.Value);
        }
    }

    [LoggerMessage(
        EventId = 5311,
        Level = LogLevel.Warning,
        Message = "Alert {AlertId} was saved but could not be published; it arrives on the next history read")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, Guid alertId);
}
