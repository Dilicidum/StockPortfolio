using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace StockPortfolio.Modules.Alerts.Api.Streaming;

/// <summary>Empty on purpose: the browser never calls the server, it only listens.</summary>
[Authorize]
public sealed class AlertsHub : Hub<IAlertClient>;
