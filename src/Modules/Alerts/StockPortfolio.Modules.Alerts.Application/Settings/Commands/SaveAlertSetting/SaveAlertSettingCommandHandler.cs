using OneOf;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Modules.Portfolio.Contracts;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Alerts.Application.Settings.Commands.SaveAlertSetting;

/// <summary>Sets a threshold on a position, or changes the one already there. One row per user and ticker.</summary>
public sealed class SaveAlertSettingCommandHandler(
    IAlertSettingRepository settings,
    IUserHoldsTicker holdings,
    AlertsOptions options)
    : ICommandHandler<
        SaveAlertSettingCommand,
        OneOf<SaveAlertSettingResult, TickerNotHeld, WindowExceedsRetention, InvalidInput>>
{
    /// <inheritdoc/>
    public Task<OneOf<SaveAlertSettingResult, TickerNotHeld, WindowExceedsRetention, InvalidInput>> Handle(
        SaveAlertSettingCommand command,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        return Ticker.Create(command.Ticker).Match(
            ticker => HandleAsync(command, ticker, ct),
            badTicker => Task.FromResult<
                OneOf<SaveAlertSettingResult, TickerNotHeld, WindowExceedsRetention, InvalidInput>>(badTicker));
    }

    private async Task<OneOf<SaveAlertSettingResult, TickerNotHeld, WindowExceedsRetention, InvalidInput>> HandleAsync(
        SaveAlertSettingCommand command,
        Ticker ticker,
        CancellationToken ct)
    {
        // "Do you own this?" is a context question, so the handler asks it - and asks it before any
        // read of the Alerts database, because a threshold on a position you do not hold is refused
        // whatever is already stored. This is the phase's only Alerts to Portfolio call.
        if (!await holdings.HoldsAsync(command.UserId, ticker.Value, ct))
        {
            return new TickerNotHeld(ticker.Value);
        }

        // The cap is configuration, not shape, so the validator cannot see it and this is a 409 rather
        // than a 400. AlertSetting still enforces it, which is what keeps the two from drifting.
        if (command.WindowMinutes > options.MaxWindowMinutes)
        {
            return new WindowExceedsRetention(command.WindowMinutes, options.MaxWindowMinutes);
        }

        var existing = await settings.FindAsync(command.UserId, ticker.Value, ct);

        if (existing is not null)
        {
            return await existing
                .Adjust(
                    command.ThresholdPercent,
                    command.WindowMinutes,
                    command.Enabled,
                    options.MaxWindowMinutes)
                .Match(
                    adjusted => SaveAndDescribeAsync(existing, ct),
                    adjustFailed => Task.FromResult<
                        OneOf<SaveAlertSettingResult, TickerNotHeld, WindowExceedsRetention, InvalidInput>>(
                        adjustFailed));
        }

        return await AlertSetting
            .Create(
                command.UserId,
                ticker.Value,
                command.ThresholdPercent,
                command.WindowMinutes,
                command.Enabled,
                options.MaxWindowMinutes)
            .Match(
                created => SaveAndDescribeAsync(created, ct),
                createFailed => Task.FromResult<
                    OneOf<SaveAlertSettingResult, TickerNotHeld, WindowExceedsRetention, InvalidInput>>(createFailed));
    }

    private async Task<OneOf<SaveAlertSettingResult, TickerNotHeld, WindowExceedsRetention, InvalidInput>>
        SaveAndDescribeAsync(AlertSetting setting, CancellationToken ct)
    {
        await settings.SaveAsync(setting, ct);

        return Describe(setting);
    }

    private static SaveAlertSettingResult Describe(AlertSetting setting) => new(
        setting.Ticker.Value,
        setting.Threshold.Value,
        setting.Window.Minutes,
        setting.Enabled);
}
