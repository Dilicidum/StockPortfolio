using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Application.Streaming;

namespace StockPortfolio.Tests.Fakes;

/// <summary>Records what was pushed, and can fail on demand — the row must survive either way.</summary>
internal sealed class FakeAlertPublisher(List<string> journal) : IAlertPublisher
{
    /// <summary>The journal entry PublishAsync writes, so persist-then-publish is an assertion about order.</summary>
    public const string Published = "published";

    private readonly List<AlertNotification> _sent = [];

    /// <summary>Gets everything pushed, in order.</summary>
    public IReadOnlyList<AlertNotification> Sent => _sent;

    /// <summary>Throws on every publish, standing in for a Redis that is down.</summary>
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
