using OneOf;
using OneOf.Types;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Application.Streaming;
using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Alerts.Application.Simulation.Commands.SimulateAlert;

/// <summary>The manual trigger, sent down the real path so what arrives proves the mechanism.</summary>
public sealed class SimulateAlertCommandHandler(
    IAlertSettingRepository settings,
    IPriceWindowReader windows,
    AlertDispatcher dispatcher,
    TimeProvider clock)
    : ICommandHandler<SimulateAlertCommand, OneOf<Success, NoPositionToSimulate>>
{
    /// <summary>What a simulated trigger costs when the ticker has no samples yet — the first minute only.</summary>
    private const decimal NominalPrice = 100m;

    /// <inheritdoc/>
    public async Task<OneOf<Success, NoPositionToSimulate>> Handle(
        SimulateAlertCommand command,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var setting = await ChooseAsync(command, ct);

        if (setting is null)
        {
            return new NoPositionToSimulate(command.Ticker);
        }

        // The live price where there is one, so the demo reads as the position it is about. A ticker
        // with an enabled threshold is a ticker the poller samples, so this is only nominal in the
        // first minute after somebody sets one.
        var window = await windows.GetWindowAsync(setting.Ticker.Value, setting.Window.Duration, ct);
        var trigger = window?.Current ?? NominalPrice;

        // A clean move of exactly the threshold, down. The reported percent IS the threshold, which is
        // the honest number for a simulation - deriving it from a rounded reference price would report
        // 4.99% for a 5% setting and look like a bug.
        var move = setting.Threshold.Value;
        var reference = decimal.Round(trigger / (1m - (move / 100m)), 2, MidpointRounding.AwayFromZero);

        var alert = FiredAlert.Record(
            setting.UserId,
            setting.Ticker,
            AlertDirection.Fall,
            changePercent: -move,
            endpointPercent: -move,
            Money.Usd(trigger),
            Money.Usd(reference),
            clock.GetUtcNow(),
            isSimulated: true);

        // No cooldown claim. A button the user just pressed must not be swallowed by a window a real
        // evaluation opened, and there is nothing to de-duplicate: they asked for exactly one.
        await dispatcher.DispatchAsync(alert, ct);

        return new Success();
    }

    /// <summary>The named threshold if it was named and is on, otherwise the caller's first one.</summary>
    private async Task<AlertSetting?> ChooseAsync(SimulateAlertCommand command, CancellationToken ct)
    {
        var enabled = (await settings.ListForUserAsync(command.UserId, ct))
            .Where(setting => setting.Enabled)
            .ToList();

        if (string.IsNullOrWhiteSpace(command.Ticker))
        {
            return enabled.Count > 0 ? enabled[0] : null;
        }

        if (!Ticker.Create(command.Ticker).TryPickT0(out var symbol, out _))
        {
            return null;
        }

        return enabled.Find(setting => setting.Ticker == symbol);
    }
}
