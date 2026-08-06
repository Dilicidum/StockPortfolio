using OneOf;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Alerts.Application.Streaming.Commands.RedeemStreamTicket;

/// <summary>Spends a ticket exactly once and answers with the user it belonged to.</summary>
public sealed class RedeemStreamTicketCommandHandler(IStreamTicketStore tickets)
    : ICommandHandler<RedeemStreamTicketCommand, OneOf<Guid, TicketNotRecognised>>
{
    /// <inheritdoc/>
    public async Task<OneOf<Guid, TicketNotRecognised>> Handle(
        RedeemStreamTicketCommand command,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Ticket))
        {
            return new TicketNotRecognised();
        }

        var userId = await tickets.RedeemAsync(command.Ticket, ct);

        return userId is null ? new TicketNotRecognised() : userId.Value;
    }
}
