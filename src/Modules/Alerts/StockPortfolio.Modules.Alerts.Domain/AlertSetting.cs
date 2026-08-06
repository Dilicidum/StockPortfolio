using OneOf;
using OneOf.Types;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Alerts.Domain;

/// <summary>One user's threshold on one ticker. A unique index on (user_id, ticker) keeps it one row.</summary>
public sealed class AlertSetting
{
    /// <summary>The only constructor. Assigns and nothing else; EF binds it by name for every row.</summary>
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

    /// <summary>Gets the identity of the setting.</summary>
    public AlertSettingId Id { get; private set; }

    /// <summary>Gets the owning user. A plain Guid: Alerts does not own the Identity module's UserId.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Gets the symbol watched, already upper-cased by Ticker.Create.</summary>
    public Ticker Ticker { get; private set; }

    /// <summary>Gets whether this threshold is evaluated at all. A disabled setting is not polled for.</summary>
    public bool Enabled { get; private set; }

    /// <summary>Gets how far the price must move before the threshold fires.</summary>
    public ThresholdPercent Threshold { get; private set; }

    /// <summary>Gets how far back the move is measured.</summary>
    public AlertWindow Window { get; private set; }

    /// <summary>Sets a threshold on a position. The only way to build an AlertSetting.</summary>
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

    /// <summary>Changes the threshold, the window and whether it is on — all three, or none of them.</summary>
    public OneOf<Success, InvalidInput> Adjust(
        decimal thresholdPercent,
        int windowMinutes,
        bool enabled,
        int maxWindowMinutes)
    {
        // Both values are checked before either is assigned: a half-applied change would leave a
        // setting nobody asked for, and there is no transaction here to roll one back.
        return Validate(thresholdPercent, windowMinutes, maxWindowMinutes).Match(
            values => Apply(values, enabled),
            invalid => invalid);
    }

    /// <summary>The one place the two configurable values are judged, so Create and Adjust cannot diverge.</summary>
    private static OneOf<(ThresholdPercent Threshold, AlertWindow Window), InvalidInput> Validate(
        decimal thresholdPercent,
        int windowMinutes,
        int maxWindowMinutes) =>
        ThresholdPercent.Create(thresholdPercent).Match(
            threshold => AlertWindow.Create(windowMinutes, maxWindowMinutes).Match(
                window => Values(threshold, window),
                badWindow => badWindow),
            badPercent => badPercent);

    /// <summary>Validate's success shape, named so both Match arms return the same type.</summary>
    private static OneOf<(ThresholdPercent Threshold, AlertWindow Window), InvalidInput> Values(
        ThresholdPercent threshold,
        AlertWindow window) => (threshold, window);

    /// <summary>Create's success shape, named so both Match arms return the same type.</summary>
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

    /// <summary>Assigns the checked values, and returns the union so both Match arms agree.</summary>
    private OneOf<Success, InvalidInput> Apply((ThresholdPercent Threshold, AlertWindow Window) values, bool enabled)
    {
        Threshold = values.Threshold;
        Window = values.Window;
        Enabled = enabled;

        return new Success();
    }
}
