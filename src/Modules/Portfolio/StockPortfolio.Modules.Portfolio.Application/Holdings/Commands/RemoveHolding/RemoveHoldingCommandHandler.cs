using OneOf;
using OneOf.Types;

using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.RemoveHolding;

/// <summary>Closes a position. Deleting the row is the whole operation — there is no domain event.</summary>
public sealed class RemoveHoldingCommandHandler(IHoldingRepository holdings)
    : ICommandHandler<RemoveHoldingCommand, OneOf<Success, NotFound>>
{
    /// <inheritdoc/>
    public async Task<OneOf<Success, NotFound>> Handle(RemoveHoldingCommand command, CancellationToken ct)
    {
        var holding = await holdings.FindByIdAsync(command.UserId, new HoldingId(command.HoldingId), ct);

        if (holding is null)
        {
            return new NotFound();
        }

        await holdings.RemoveAsync(holding, ct);

        return new Success();
    }
}
