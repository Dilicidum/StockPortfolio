using System.Net;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Api.Extensions;
using StockPortfolio.Api.IntegrationTests.Infrastructure;
using StockPortfolio.Modules.Alerts.Api;
using StockPortfolio.Modules.Alerts.Api.Streaming;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>The three things about the alert hub that are ours rather than SignalR's.
///
/// The fan-out across replicas is no longer tested here and deliberately so: it is the Redis
/// backplane, which is Microsoft's code and configuration, not ours. What IS ours is reading the
/// token out of the query string, refusing to do that anywhere else, and naming the claim that
/// decides who a message is for.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class AlertStreamTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>The hub carries [Authorize], so an unauthenticated request must not reach it.</summary>
    [Fact]
    public async Task TheHubPath_WithNoToken_IsRefused()
    {
        using var client = _fixture.CreateClient();

        using var response = await Wire.SendAsync(client, HttpMethod.Get, AlertsEndpoints.HubPath);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }

    /// <summary>THE test for the query-string token. A browser cannot send the header, so this is the only way in.</summary>
    [Fact]
    public async Task TheHubPath_WithTheTokenInTheQueryString_GetsPastAuthentication()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "hub-query-token");

        // The real path the browser opens, not /negotiate — the client skips negotiation, so a test
        // driving that route would keep passing if the hook were narrowed to negotiate alone.
        using var response = await Wire.SendAsync(
            client,
            HttpMethod.Get,
            $"{AlertsEndpoints.HubPath}?access_token={Uri.EscapeDataString(token)}");

        // Not 200: this is a plain GET rather than a WebSocket handshake, so SignalR refuses it on
        // its own terms. Anything other than 401 means authentication accepted the token, which is
        // the whole of what this test is for. Delete the OnMessageReceived hook and it goes 401,
        // the SPA reconnects for ever, and no alert is ever delivered with nothing failing.
        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }

    /// <summary>The hook is path-scoped, and this is the test that keeps it that way.</summary>
    [Fact]
    public async Task AnOrdinaryRoute_WithTheTokenInTheQueryString_IsStillRefused()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "query-token-scope");

        using var response = await Wire.SendAsync(
            client,
            HttpMethod.Get,
            $"/api/alerts/settings?access_token={Uri.EscapeDataString(token)}");

        // Drop the StartsWithSegments check and this becomes a 200 — at which point every route in
        // the app can be called with the token in the URL, and every access log holds a live credential.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }

    /// <summary>The silent failure: a provider reading the wrong claim delivers every alert to nobody.</summary>
    [Fact]
    public void TheUserIdProvider_IsOurs_AndReadsTheClaimTheTokensActuallyCarry()
    {
        _fixture.Services.GetRequiredService<IUserIdProvider>()
            .ShouldBeOfType<SubjectClaimUserIdProvider>(
                "the built-in provider reads `nameidentifier`, which these tokens do not carry. "
                    + "Clients.User then matches nothing and every alert is delivered to no one, "
                    + "with no exception and no log line anywhere.");

        SubjectClaimUserIdProvider.SubjectClaimType.ShouldBe(
            AuthenticationExtensions.UserIdClaimType,
            "the claim the tokens are issued with and the claim the hub matches on are two settings "
                + "in two files, and nothing except this line makes them agree.");
    }

    private static async Task<string> SignedInAsync(HttpClient client, string prefix) =>
        (await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail(prefix))).AccessToken;
}
