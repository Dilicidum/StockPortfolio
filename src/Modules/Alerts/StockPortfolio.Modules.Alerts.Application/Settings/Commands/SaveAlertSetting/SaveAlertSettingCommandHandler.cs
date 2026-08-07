using OneOf;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Modules.Portfolio.Contracts;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Alerts.Application.Settings.Commands.SaveAlertSetting;

public sealed class SaveAlertSettingCommandHandler(
    IAlertSettingRepository settings,
    IUserHoldsTicker holdings,
    AlertsOptions options)
    : ICommandHandler<
        SaveAlertSettingCommand,
        OneOf<SaveAlertSettingResult, TickerNotHeld, WindowExceedsRetention, InvalidInput>>
{
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
        if (!await holdings.HoldsAsync(command.UserId, ticker.Value, ct))
        {
            return new TickerNotHeld(ticker.Value);
        }

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
