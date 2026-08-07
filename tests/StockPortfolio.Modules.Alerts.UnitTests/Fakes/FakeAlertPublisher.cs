using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Application.Streaming;

namespace StockPortfolio.Tests.Fakes;

internal sealed class FakeAlertPublisher(List<string> journal) : IAlertPublisher
{
    public const string Published = "published";

    private readonly List<AlertNotification> _sent = [];

    public IReadOnlyList<AlertNotification> Sent => _sent;

    public bool ThrowEveryTime { get; init; }

    public Task PublishAsync(AlertNotification notification, CancellationToken ct)
    {
        journal.Add(Published);

        if (ThrowEveryTime)
        {
            throw new InvalidOperationException("The publisher is unavailable.");
        }

        _sent.Add(notification);

        return Task.CompletedTask;
    }
}
