using Microsoft.AspNetCore.SignalR;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Application.Streaming;

namespace StockPortfolio.Modules.Alerts.Api.Streaming;

public sealed class SignalRAlertPublisher(IHubContext<AlertsHub, IAlertClient> hub) : IAlertPublisher
{
    public Task PublishAsync(AlertNotification notification, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return hub.Clients.User(notification.UserId.ToString("D")).AlertFired(notification);
    }
}
