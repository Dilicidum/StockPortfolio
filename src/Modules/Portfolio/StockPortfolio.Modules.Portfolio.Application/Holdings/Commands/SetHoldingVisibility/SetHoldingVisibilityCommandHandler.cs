using OneOf;
using OneOf.Types;

using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.SetHoldingVisibility;

/// <summary>Shows or hides a position without touching quantity, price or alerts.</summary>
public sealed class SetHoldingVisibilityCommandHandler(IHoldingRepository holdings, TimeProvider clock)
    : ICommandHandler<SetHoldingVisibilityCommand, OneOf<Success, NotFound>>
{
    /// <inheritdoc/>
    public async Task<OneOf<Success, NotFound>> Handle(SetHoldingVisibilityCommand command, CancellationToken ct)
    {
        // Scoped to the user by the repository, so another user's id is NotFound and never Forbidden.
        var holding = await holdings.FindByIdAsync(command.UserId, new HoldingId(command.HoldingId), ct);

        if (holding is null)
        {
            return new NotFound();
        }

        holding.SetVisibility(command.IsVisible, clock);

        await holdings.UpdateAsync(holding, ct);

        return new Success();
    }
}
