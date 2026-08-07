using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Alerts.Domain;

public sealed class FiredAlert
{
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

    public FiredAlertId Id { get; private set; }

    public Guid UserId { get; private set; }

    public Ticker Ticker { get; private set; }

    public AlertDirection Direction { get; private set; }

    public decimal ChangePercent { get; private set; }

    public decimal EndpointPercent { get; private set; }

    public Money TriggerPrice { get; private set; }

    public Money ReferencePrice { get; private set; }

    public DateTimeOffset FiredAt { get; private set; }

    public bool IsSimulated { get; private set; }

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

        alert.TriggerPrice = triggerPrice;
        alert.ReferencePrice = referencePrice;

        return alert;
    }
}
