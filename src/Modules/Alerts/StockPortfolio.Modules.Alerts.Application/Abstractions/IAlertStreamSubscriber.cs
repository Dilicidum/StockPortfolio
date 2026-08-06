using System.Threading.Channels;

using StockPortfolio.Modules.Alerts.Application.Streaming;

namespace StockPortfolio.Modules.Alerts.Application.Abstractions;

/// <summary>The receiving half of the fan-out: this replica listens for one user's alerts.</summary>
public interface IAlertStreamSubscriber
{
    /// <summary>Feeds the writer until the returned handle is disposed, which unsubscribes.</summary>
    Task<IAsyncDisposable> SubscribeAsync(
        Guid userId,
        ChannelWriter<AlertNotification> writer,
        CancellationToken ct);
}
