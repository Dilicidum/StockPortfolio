using StockPortfolio.Modules.Alerts.Application.Streaming;

namespace StockPortfolio.Modules.Alerts.Api.Streaming;

/// <summary>What the server can call on a connected browser, and the name it listens for.</summary>
public interface IAlertClient
{
    /// <summary>One threshold crossing, pushed to every tab this user has open.</summary>
    Task AlertFired(AlertNotification notification);
}
