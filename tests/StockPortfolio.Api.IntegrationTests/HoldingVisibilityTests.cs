using System.Net;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>Hiding a position is a display filter over /api/holdings/{id}/visibility.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class HoldingVisibilityTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    // Hiding drops the row from the dashboard but leaves it, marked hidden, on the holdings list.
    [Fact]
    public async Task Patch_ToHidden_RemovesFromDashboard_ButStaysOnHoldingsList()
    {
        var (client, token) = await SignedInAsync("visibility-hide");

        var shown = Wire.UniqueTicker();
        var hidden = Wire.UniqueTicker();

        await AddSucceedsAsync(client, token, shown, 10m, 100m);
        var hiddenId = await AddSucceedsAsync(client, token, hidden, 5m, 50m);

        using var response = await SetVisibilityAsync(client, token, hiddenId, isVisible: false);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await Wire.Describe(response));

        var dashboard = await Wire.GetDashboardAsync(client, token);
        dashboard.Positions.ShouldHaveSingleItem().Ticker.ShouldBe(shown);

        var holdings = await Wire.ListHoldingsAsync(client, token);
        holdings.Count.ShouldBe(2, "hiding must not delete the position, only filter it off the dashboard");
        holdings.Single(h => h.Id == hiddenId).IsVisible.ShouldBeFalse();
    }

    // A 404, never a 403: a 403 would confirm to a stranger that this id exists.
    [Fact]
    public async Task Patch_AHoldingOwnedBySomeoneElse_Returns404()
    {
        var (ownerClient, ownerToken) = await SignedInAsync("visibility-owner");
        var id = await AddSucceedsAsync(ownerClient, ownerToken, Wire.UniqueTicker(), 5m, 200m);

        var (strangerClient, strangerToken) = await SignedInAsync("visibility-stranger");

        using var response = await SetVisibilityAsync(strangerClient, strangerToken, id, isVisible: false);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, await Wire.Describe(response));

        // The owner's row is untouched, so the 404 was a refusal rather than a silent success.
        (await Wire.ListHoldingsAsync(ownerClient, ownerToken)).ShouldHaveSingleItem().IsVisible.ShouldBeTrue();
    }

    // The assertion this task exists for: IUserHoldsTicker deliberately ignores visibility, because a
    // hidden position is still held and an alert on it must still fire. An implementation that filtered
    // HoldsAsync on IsVisible would turn this 200 into a 409 and every other test here would stay green.
    [Fact]
    public async Task Patch_ToHidden_StillLetsAnAlertBeConfigured()
    {
        var (client, token) = await SignedInAsync("visibility-alert");

        var ticker = await AddPositionAsync(client, token, Wire.UniqueTicker(), 10m, 100m);

        using var hide = await SetVisibilityAsync(client, token, ticker.Id, isVisible: false);
        hide.StatusCode.ShouldBe(HttpStatusCode.NoContent, await Wire.Describe(hide));

        using var settingResponse = await Wire.SaveAlertSettingAsync(client, token, ticker.Ticker, 5m, 30);

        settingResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(settingResponse));
    }

    private static Task<HttpResponseMessage> SetVisibilityAsync(
        HttpClient client,
        string accessToken,
        Guid id,
        bool isVisible) =>
        Wire.SendAsync(
            client,
            HttpMethod.Patch,
            $"/api/holdings/{id}/visibility",
            accessToken,
            new { isVisible });

    private static async Task<Guid> AddSucceedsAsync(
        HttpClient client,
        string accessToken,
        string ticker,
        decimal quantity,
        decimal price)
    {
        var created = await AddPositionAsync(client, accessToken, ticker, quantity, price);

        return created.Id;
    }

    private static async Task<HoldingPayload> AddPositionAsync(
        HttpClient client,
        string accessToken,
        string ticker,
        decimal quantity,
        decimal price)
    {
        using var response = await Wire.AddHoldingAsync(client, accessToken, ticker, quantity, price);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await Wire.Describe(response));

        return (await Wire.ListHoldingsAsync(client, accessToken)).Single(h => h.Ticker == ticker);
    }

    private async Task<(HttpClient Client, string Token)> SignedInAsync(string prefix)
    {
        var client = _fixture.CreateClient();
        var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail(prefix));

        return (client, tokens.AccessToken);
    }
}
