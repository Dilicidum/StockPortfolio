using System.Security.Claims;

using Microsoft.AspNetCore.SignalR;

namespace StockPortfolio.Modules.Alerts.Api.Streaming;

public sealed class SubjectClaimUserIdProvider : IUserIdProvider
{
    public const string SubjectClaimType = "sub";

    public string? GetUserId(HubConnectionContext connection) =>
        connection?.User?.FindFirstValue(SubjectClaimType);
}
