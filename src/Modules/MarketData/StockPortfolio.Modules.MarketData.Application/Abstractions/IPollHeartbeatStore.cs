using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

/// <summary>Writes at the end of a poll cycle and swallows its own failures; a heartbeat must never end a cycle.</summary>
public interface IPollHeartbeatStore
{
    Task WriteAsync(PollHeartbeat heartbeat, CancellationToken ct);

    Task<PollHeartbeat?> ReadAsync(CancellationToken ct);
}
