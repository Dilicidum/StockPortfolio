using System.Globalization;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Tests.Fakes;

/// <summary>Set-if-absent against a clock, which is the only behaviour the real store has.</summary>
internal sealed class FakeAlertCooldownStore(TimeProvider clock) : IAlertCooldownStore
{
    private readonly Dictionary<string, DateTimeOffset> _held = new(StringComparer.Ordinal);

    /// <summary>Gets how many claims were attempted, so "asked before writing" is assertable.</summary>
    public int Attempts { get; private set; }

    /// <summary>Refuses every claim, standing in for a Redis that cannot answer.</summary>
    public bool RefuseEverything { get; init; }

    public Task<bool> TryStartAsync(
        Guid userId,
        string ticker,
        AlertDirection direction,
        TimeSpan cooldown,
        CancellationToken ct)
    {
        Attempts++;

        if (RefuseEverything)
        {
            return Task.FromResult(false);
        }

        var key = string.Create(CultureInfo.InvariantCulture, $"{userId:D}:{ticker}:{direction}");
        var now = clock.GetUtcNow();

        if (_held.TryGetValue(key, out var until) && until > now)
        {
            return Task.FromResult(false);
        }

        _held[key] = now + cooldown;

        return Task.FromResult(true);
    }
}
