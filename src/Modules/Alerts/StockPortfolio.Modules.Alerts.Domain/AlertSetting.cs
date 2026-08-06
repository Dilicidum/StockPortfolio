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
        int maxWindowMinutes)
    {
        if (Ticker.Create(ticker).TryPickT1(out var badTicker, out var symbol))
        {
            return badTicker;
        }

        if (Validate(thresholdPercent, windowMinutes, maxWindowMinutes)
            .TryPickT1(out var invalid, out var values))
        {
            return invalid;
        }

        return new AlertSetting(
            AlertSettingId.New(),
            userId,
            symbol,
            enabled,
            values.Threshold,
            values.Window);
    }

    /// <summary>Changes the threshold, the window and whether it is on — all three, or none of them.</summary>
    public OneOf<Success, InvalidInput> Adjust(
        decimal thresholdPercent,
        int windowMinutes,
        bool enabled,
        int maxWindowMinutes)
    {
        // Both values are checked before either is assigned: a half-applied change would leave a
        // setting nobody asked for, and there is no transaction here to roll one back.
        if (Validate(thresholdPercent, windowMinutes, maxWindowMinutes)
            .TryPickT1(out var invalid, out var values))
        {
            return invalid;
        }

        Threshold = values.Threshold;
        Window = values.Window;
        Enabled = enabled;

        return new Success();
    }

    /// <summary>The one place the two configurable values are judged, so Create and Adjust cannot diverge.</summary>
    private static OneOf<(ThresholdPercent Threshold, AlertWindow Window), InvalidInput> Validate(
        decimal thresholdPercent,
        int windowMinutes,
        int maxWindowMinutes)
    {
        if (ThresholdPercent.Create(thresholdPercent).TryPickT1(out var badPercent, out var threshold))
        {
            return badPercent;
        }

        if (AlertWindow.Create(windowMinutes, maxWindowMinutes).TryPickT1(out var badWindow, out var window))
        {
            return badWindow;
        }

        return (threshold, window);
    }
}
