using System.Net;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Host.Extensions;
using StockPortfolio.Api.IntegrationTests.Infrastructure;
using StockPortfolio.Modules.Alerts.Api;
using StockPortfolio.Modules.Alerts.Api.Streaming;

namespace StockPortfolio.Api.IntegrationTests;

// The fan-out across replicas is Microsoft's Redis backplane and is deliberately not tested here.
[Collection(ApiCollectionDefinition.Name)]
public sealed class AlertStreamTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public async Task TheHubPath_WithNoToken_IsRefused()
    {
        using var client = _fixture.CreateClient();

        using var response = await Wire.SendAsync(client, HttpMethod.Get, AlertsEndpoints.HubPath);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }

    // A browser cannot set a header on the hub connection, so the query string is the only way in.
    [Fact]
    public async Task TheHubPath_WithTheTokenInTheQueryString_GetsPastAuthentication()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "hub-query-token");

        // The real path the browser opens, not /negotiate: the client skips negotiation, so driving /negotiate would pass a narrowed hook.
        using var response = await Wire.SendAsync(
            client,
            HttpMethod.Get,
            $"{AlertsEndpoints.HubPath}?access_token={Uri.EscapeDataString(token)}");

        // Anything but 401 means authentication accepted the token; delete the OnMessageReceived hook and the SPA reconnects for ever in silence.
        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }

    [Fact]
    public async Task AnOrdinaryRoute_WithTheTokenInTheQueryString_IsStillRefused()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "query-token-scope");

        using var response = await Wire.SendAsync(
            client,
            HttpMethod.Get,
            $"/api/alerts/settings?access_token={Uri.EscapeDataString(token)}");

        // Drop the StartsWithSegments check and every route accepts a token in its URL, so every access log holds a live credential.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }

    // A provider reading the wrong claim delivers every alert to nobody, with no exception and no log line.
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
