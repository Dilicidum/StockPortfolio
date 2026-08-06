using StackExchange.Redis;

using StockPortfolio.Modules.Alerts.Application.Abstractions;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Redis;

/// <summary>Tickets in Redis rather than in memory, because the replica that issues one rarely serves it.</summary>
internal sealed class RedisStreamTicketStore(IConnectionMultiplexer multiplexer) : IStreamTicketStore
{
    private const string KeyPrefix = "alerts:ticket:";

    public async Task IssueAsync(string ticket, Guid userId, TimeSpan lifetime, CancellationToken ct)
        => await multiplexer.GetDatabase().StringSetAsync(KeyPrefix + ticket, userId.ToString("D"), lifetime);

    public async Task<Guid?> RedeemAsync(string ticket, CancellationToken ct)
    {
        // ONE operation. A GET followed by a DEL lets two connections both read the ticket before
        // either deletes it, and single-use is the entire security property this thing has.
        var stored = await multiplexer.GetDatabase().StringGetDeleteAsync(KeyPrefix + ticket);

        // (string?), because RedisValue converts implicitly to both a string and a byte span and the
        // overload resolution is otherwise ambiguous rather than merely surprising.
        return Guid.TryParse((string?)stored, out var userId) ? userId : null;
    }
}
