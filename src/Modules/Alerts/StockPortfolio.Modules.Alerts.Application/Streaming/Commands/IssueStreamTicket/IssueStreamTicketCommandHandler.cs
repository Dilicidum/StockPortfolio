using System.Buffers.Text;
using System.Security.Cryptography;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Alerts.Application.Streaming.Commands.IssueStreamTicket;

/// <summary>Mints a single-use ticket. There is no failure case: the bearer token already decided.</summary>
public sealed class IssueStreamTicketCommandHandler(IStreamTicketStore tickets, TimeProvider clock)
    : ICommandHandler<IssueStreamTicketCommand, IssueStreamTicketResult>
{
    /// <summary>Thirty seconds is long enough for one page load and short enough that a leaked URL is stale.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    /// <summary>256 bits, because this value travels in a query string and lands in access logs.</summary>
    private const int TicketBytes = 32;

    /// <inheritdoc/>
    public async Task<IssueStreamTicketResult> Handle(IssueStreamTicketCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Base64Url, not Base64: the ticket is a query-string value, and '+' and '/' would need
        // escaping that some client somewhere would forget.
        var ticket = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TicketBytes));

        await tickets.IssueAsync(ticket, command.UserId, Lifetime, ct);

        return new IssueStreamTicketResult(ticket, clock.GetUtcNow() + Lifetime);
    }
}
