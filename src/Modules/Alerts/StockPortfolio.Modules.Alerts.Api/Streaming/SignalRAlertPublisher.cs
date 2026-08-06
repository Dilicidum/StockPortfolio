using Microsoft.AspNetCore.SignalR;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Application.Streaming;

namespace StockPortfolio.Modules.Alerts.Api.Streaming;

/// <summary>Publishes down SignalR, whose Redis backplane carries the message to the replica holding the connection.</summary>
public sealed class SignalRAlertPublisher(IHubContext<AlertsHub, IAlertClient> hub) : IAlertPublisher
{
    /// <summary>Fire and forget by design: an alert nobody is connected for is already saved.</summary>
    public Task PublishAsync(AlertNotification notification, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notification);

        // "D" is the format the user id provider reads back off the claim, and the two must agree
        // exactly - SignalR matches user ids as strings, so a different format delivers to nobody.
        return hub.Clients.User(notification.UserId.ToString("D")).AlertFired(notification);
    }
}
