using OneOf;

using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.AddHolding;

/// <summary>Opens a position, or merges the purchase into the one that already exists.</summary>
public sealed class AddHoldingCommandHandler(
    IHoldingRepository holdings,
    ISymbolValidator symbols,
    TimeProvider clock)
    : ICommandHandler<AddHoldingCommand, OneOf<HoldingCreated, HoldingMerged, InvalidInput, UnknownTicker>>
{
    /// <inheritdoc/>
    public async Task<OneOf<HoldingCreated, HoldingMerged, InvalidInput, UnknownTicker>> Handle(
        AddHoldingCommand command,
        CancellationToken ct)
    {
        if (!Ticker.Create(command.Ticker).TryPickT0(out var ticker, out _))
        {
            return new UnknownTicker(command.Ticker);
        }

        // Shape first, then existence. IsKnownSymbolAsync answers true when the provider cannot answer,
        // which is the whole mitigation for putting an outbound call on a write path.
        if (!await symbols.IsKnownSymbolAsync(ticker.Value, ct))
        {
            return new UnknownTicker(ticker.Value);
        }

        var price = Money.Usd(command.Price);

        // "Do I already hold this?" is a context question, so the handler asks it — it does not read
        // a SQLSTATE back out of an exception. Two truly simultaneous requests can both pass here;
        // the unique index is the real guarantee and the loser surfaces as 500. See docs/plan §2.6.
        var existing = await holdings.FindAsync(command.UserId, ticker, ct);

        if (existing is not null)
        {
            if (!existing.Merge(command.Quantity, price, clock).TryPickT0(out _, out var mergeFailed))
            {
                return mergeFailed;
            }

            await holdings.UpdateAsync(existing, ct);

            return new HoldingMerged(HoldingSummary.From(existing));
        }

        if (!Holding.Create(command.UserId, ticker, command.Quantity, price, clock)
                .TryPickT0(out var created, out var createFailed))
        {
            return createFailed;
        }

        await holdings.AddAsync(created, ct);

        return new HoldingCreated(HoldingSummary.From(created));
    }
}
