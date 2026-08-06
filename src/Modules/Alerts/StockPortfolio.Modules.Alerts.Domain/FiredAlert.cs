using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Alerts.Domain;

/// <summary>One breach that happened. Written once and never changed; the history endpoint reads it back.</summary>
public sealed class FiredAlert
{
    /// <summary>The only constructor. Assigns and nothing else; EF binds it by name for every row.</summary>
    private FiredAlert(
        FiredAlertId id,
        Guid userId,
        Ticker ticker,
        AlertDirection direction,
        decimal changePercent,
        decimal endpointPercent,
        DateTimeOffset firedAt,
        bool isSimulated)
    {
        Id = id;
        UserId = userId;
        Ticker = ticker;
        Direction = direction;
        ChangePercent = changePercent;
        EndpointPercent = endpointPercent;
        FiredAt = firedAt;
        IsSimulated = isSimulated;
    }

    /// <summary>Gets the identity of the alert.</summary>
    public FiredAlertId Id { get; private set; }

    /// <summary>Gets the user the alert belongs to. A plain Guid: Alerts does not own UserId.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Gets the symbol that moved.</summary>
    public Ticker Ticker { get; private set; }

    /// <summary>Gets which way it moved.</summary>
    public AlertDirection Direction { get; private set; }

    /// <summary>Gets the move against the window extreme, signed. This is the number the user is told.</summary>
    public decimal ChangePercent { get; private set; }

    /// <summary>Gets the move from the oldest sample to the newest, signed — what the extreme was checked against.</summary>
    public decimal EndpointPercent { get; private set; }

    /// <summary>Gets the price at the moment it fired. Omitted from the constructor — efcore#31621.</summary>
    public Money TriggerPrice { get; private set; }

    /// <summary>Gets the window extreme it was measured against. Omitted from the constructor — efcore#31621.</summary>
    public Money ReferencePrice { get; private set; }

    /// <summary>Gets the instant the breach was recorded.</summary>
    public DateTimeOffset FiredAt { get; private set; }

    /// <summary>Gets whether the Simulate button produced this rather than a real evaluation.</summary>
    public bool IsSimulated { get; private set; }

    /// <summary>Records a breach. Every value has already been judged by the evaluator, so nothing is rechecked.</summary>
    public static FiredAlert Record(
        Guid userId,
        Ticker ticker,
        AlertDirection direction,
        decimal changePercent,
        decimal endpointPercent,
        Money triggerPrice,
        Money referencePrice,
        DateTimeOffset firedAt,
        bool isSimulated)
    {
        var alert = new FiredAlert(
            FiredAlertId.New(),
            userId,
            ticker,
            direction,
            changePercent,
            endpointPercent,
            firedAt,
            isSimulated);

        // Assigned after construction because a complex type cannot be a constructor parameter
        // (efcore#31621). private set is reachable from inside the type.
        alert.TriggerPrice = triggerPrice;
        alert.ReferencePrice = referencePrice;

        return alert;
    }
}
