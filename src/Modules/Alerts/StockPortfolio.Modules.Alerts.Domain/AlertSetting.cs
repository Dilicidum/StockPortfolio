using OneOf;
using OneOf.Types;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Alerts.Domain;

public sealed class AlertSetting
{
    private AlertSetting(
        AlertSettingId id,
        Guid userId,
        Ticker ticker,
        bool enabled,
        ThresholdPercent threshold,
        AlertWindow window)
    {
        Id = id;
        UserId = userId;
        Ticker = ticker;
        Enabled = enabled;
        Threshold = threshold;
        Window = window;
    }

    public AlertSettingId Id { get; private set; }

    public Guid UserId { get; private set; }

    public Ticker Ticker { get; private set; }

    public bool Enabled { get; private set; }

    public ThresholdPercent Threshold { get; private set; }

    public AlertWindow Window { get; private set; }

    public static OneOf<AlertSetting, InvalidInput> Create(
        Guid userId,
        string ticker,
        decimal thresholdPercent,
        int windowMinutes,
        bool enabled,
        int maxWindowMinutes) =>
        Ticker.Create(ticker).Match(
            symbol => Validate(thresholdPercent, windowMinutes, maxWindowMinutes).Match(
                values => Build(userId, symbol, enabled, values),
                invalid => invalid),
            badTicker => badTicker);

    public OneOf<Success, InvalidInput> Adjust(
        decimal thresholdPercent,
        int windowMinutes,
        bool enabled,
        int maxWindowMinutes)
    {
        return Validate(thresholdPercent, windowMinutes, maxWindowMinutes).Match(
            values => Apply(values, enabled),
            invalid => invalid);
    }

    private static OneOf<(ThresholdPercent Threshold, AlertWindow Window), InvalidInput> Validate(
        decimal thresholdPercent,
        int windowMinutes,
        int maxWindowMinutes) =>
        ThresholdPercent.Create(thresholdPercent).Match(
            threshold => AlertWindow.Create(windowMinutes, maxWindowMinutes).Match(
                window => Values(threshold, window),
                badWindow => badWindow),
            badPercent => badPercent);

    private static OneOf<(ThresholdPercent Threshold, AlertWindow Window), InvalidInput> Values(
        ThresholdPercent threshold,
        AlertWindow window) => (threshold, window);

    private static OneOf<AlertSetting, InvalidInput> Build(
        Guid userId,
        Ticker symbol,
        bool enabled,
        (ThresholdPercent Threshold, AlertWindow Window) values) =>
        new AlertSetting(
            AlertSettingId.New(),
            userId,
            symbol,
            enabled,
            values.Threshold,
            values.Window);

    private OneOf<Success, InvalidInput> Apply((ThresholdPercent Threshold, AlertWindow Window) values, bool enabled)
    {
        Threshold = values.Threshold;
        Window = values.Window;
        Enabled = enabled;

        return new Success();
    }
}
