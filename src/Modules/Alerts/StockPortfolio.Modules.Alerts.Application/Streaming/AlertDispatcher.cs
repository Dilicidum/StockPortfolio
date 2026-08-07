using Microsoft.Extensions.Logging;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Application.Streaming;

public sealed partial class AlertDispatcher(
    IFiredAlertRepository firedAlerts,
    IAlertPublisher publisher,
    ILogger<AlertDispatcher> logger)
{
    public async Task DispatchAsync(FiredAlert alert, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(alert);

        await firedAlerts.AddAsync(alert, ct);

        try
        {
            await publisher.PublishAsync(AlertNotification.From(alert), ct);
        }

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
