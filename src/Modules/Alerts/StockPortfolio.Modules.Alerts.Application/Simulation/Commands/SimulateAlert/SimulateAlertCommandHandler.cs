using OneOf;
using OneOf.Types;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Application.Streaming;
using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Alerts.Application.Simulation.Commands.SimulateAlert;

public sealed class SimulateAlertCommandHandler(
    IAlertSettingRepository settings,
    IPriceWindowReader windows,
    AlertDispatcher dispatcher,
    TimeProvider clock)
    : ICommandHandler<SimulateAlertCommand, OneOf<Success, NoPositionToSimulate>>
{
    private const decimal NominalPrice = 100m;

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

        var window = await windows.GetWindowAsync(setting.Ticker.Value, setting.Window.Duration, ct);
        var trigger = window?.Current ?? NominalPrice;

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

        // No cooldown claim: a button the user just pressed must not be swallowed by a window a real evaluation opened.
        await dispatcher.DispatchAsync(alert, ct);

        return new Success();
    }

    private async Task<AlertSetting?> ChooseAsync(SimulateAlertCommand command, CancellationToken ct)
    {
        var enabled = (await settings.ListForUserAsync(command.UserId, ct))
            .Where(setting => setting.Enabled)
            .ToList();

        if (string.IsNullOrWhiteSpace(command.Ticker))
        {
            return enabled.Count > 0 ? enabled[0] : null;
        }

        return Ticker.Create(command.Ticker).Match(
            symbol => enabled.Find(setting => setting.Ticker == symbol),
            badTicker => null);
    }
}
