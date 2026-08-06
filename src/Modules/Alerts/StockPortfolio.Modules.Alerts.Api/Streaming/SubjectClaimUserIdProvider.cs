using System.Security.Claims;

using Microsoft.AspNetCore.SignalR;

namespace StockPortfolio.Modules.Alerts.Api.Streaming;

/// <summary>Reads the user id from `sub`, because the built-in provider reads a claim these tokens do not carry.</summary>
public sealed class SubjectClaimUserIdProvider : IUserIdProvider
{
    /// <summary>The claim carrying the user id, the same name every other module reads.</summary>
    public const string SubjectClaimType = "sub";

    // Without this the default provider looks for `nameidentifier`, finds nothing, and Clients.User
    // silently delivers to no one. Nothing fails, nothing logs; alerts just never arrive.
    public string? GetUserId(HubConnectionContext connection) =>
        connection?.User?.FindFirstValue(SubjectClaimType);
}
