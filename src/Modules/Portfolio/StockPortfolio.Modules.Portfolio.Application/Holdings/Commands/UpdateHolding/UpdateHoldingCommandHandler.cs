using OneOf;
using OneOf.Types;

using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.UpdateHolding;

public sealed class UpdateHoldingCommandHandler(IHoldingRepository holdings, TimeProvider clock)
    : ICommandHandler<UpdateHoldingCommand, OneOf<HoldingSummary, NotFound, InvalidInput>>
{
    public async Task<OneOf<HoldingSummary, NotFound, InvalidInput>> Handle(
        UpdateHoldingCommand command,
        CancellationToken ct)
    {
        var holding = await holdings.FindByIdAsync(command.UserId, new HoldingId(command.HoldingId), ct);

        if (holding is null)
        {
            return new NotFound();
        }

        return await holding.Correct(command.Quantity, Money.Usd(command.Price), clock).Match(
            corrected => SaveAsync(holding, ct),
            invalid => Task.FromResult<OneOf<HoldingSummary, NotFound, InvalidInput>>(invalid));
    }

    private async Task<OneOf<HoldingSummary, NotFound, InvalidInput>> SaveAsync(
        Holding holding,
        CancellationToken ct)
    {
        await holdings.UpdateAsync(holding, ct);

        return HoldingSummary.From(holding);
    }
}
