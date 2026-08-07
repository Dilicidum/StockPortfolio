using OneOf;

using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.AddHolding;

public sealed class AddHoldingCommandHandler(
    IHoldingRepository holdings,
    ISymbolValidator symbols,
    TimeProvider clock)
    : ICommandHandler<AddHoldingCommand, OneOf<HoldingCreated, HoldingMerged, InvalidInput, UnknownTicker>>
{
    public Task<OneOf<HoldingCreated, HoldingMerged, InvalidInput, UnknownTicker>> Handle(
        AddHoldingCommand command,
        CancellationToken ct) =>
        Ticker.Create(command.Ticker).Match(
            ticker => HandleAsync(command, ticker, ct),
            badTicker => Failed(new UnknownTicker(command.Ticker)));

    private static Task<OneOf<HoldingCreated, HoldingMerged, InvalidInput, UnknownTicker>> Failed(
        OneOf<HoldingCreated, HoldingMerged, InvalidInput, UnknownTicker> failure) =>
        Task.FromResult(failure);

    private async Task<OneOf<HoldingCreated, HoldingMerged, InvalidInput, UnknownTicker>> HandleAsync(
        AddHoldingCommand command,
        Ticker ticker,
        CancellationToken ct)
    {
        if (!await symbols.IsKnownSymbolAsync(ticker.Value, ct))
        {
            return new UnknownTicker(ticker.Value);
        }

        var price = Money.Usd(command.Price);

        var existing = await holdings.FindAsync(command.UserId, ticker, ct);

        if (existing is not null)
        {
            return await existing.Merge(command.Quantity, price, clock).Match(
                merged => SaveMergedAsync(existing, ct),
                mergeFailed => Failed(mergeFailed));
        }

        return await Holding.Create(command.UserId, ticker, command.Quantity, price, clock).Match(
            created => SaveCreatedAsync(created, ct),
            createFailed => Failed(createFailed));
    }

    private async Task<OneOf<HoldingCreated, HoldingMerged, InvalidInput, UnknownTicker>> SaveMergedAsync(
        Holding existing,
        CancellationToken ct)
    {
        await holdings.UpdateAsync(existing, ct);

        return new HoldingMerged(HoldingSummary.From(existing));
    }

    private async Task<OneOf<HoldingCreated, HoldingMerged, InvalidInput, UnknownTicker>> SaveCreatedAsync(
        Holding created,
        CancellationToken ct)
    {
        await holdings.AddAsync(created, ct);

        return new HoldingCreated(HoldingSummary.From(created));
    }
}
