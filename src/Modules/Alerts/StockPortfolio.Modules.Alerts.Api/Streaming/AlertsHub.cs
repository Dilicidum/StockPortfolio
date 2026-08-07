using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace StockPortfolio.Modules.Alerts.Api.Streaming;

[Authorize]
public sealed class AlertsHub : Hub<IAlertClient>;
