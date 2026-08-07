using StockPortfolio.Modules.Alerts.Application.Streaming;

namespace StockPortfolio.Modules.Alerts.Api.Streaming;

public interface IAlertClient
{
    Task AlertFired(AlertNotification notification);
}
