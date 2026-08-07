using System.Net;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

[Collection(ApiCollectionDefinition.Name)]
public sealed class AlertSimulationTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public async Task Simulating_WritesARowThatTheHistoryReadFinds_Badged()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "simulate-history");

        var ticker = await WatchedPositionAsync(client, token);

        using (var accepted = await Wire.SimulateAlertAsync(client, token))
        {
            accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await Wire.Describe(accepted));
        }

        var row = (await Wire.ListFiredAlertsAsync(client, token)).ShouldHaveSingleItem();

        row.Ticker.ShouldBe(ticker);
        row.IsSimulated.ShouldBeTrue();

        // "Fall", never 0: JsonStringEnumConverter is registered, and the client's union is of strings.
        row.Direction.ShouldBe("Fall");
        row.ChangePercent.ShouldBe("-5.00");
        row.Reason.ShouldContain("fell");
        row.TriggerPrice.Currency.ShouldBe("USD");
        row.ReferencePrice.Amount.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Simulating_WithNoThreshold_IsRejected()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "simulate-nothing");

        using var response = await Wire.SimulateAlertAsync(client, token);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await Wire.Describe(response));

        (await Wire.ListFiredAlertsAsync(client, token)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Simulating_ANamedTicker_FiresThatOne()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "simulate-named");

        var first = await WatchedPositionAsync(client, token);
        var second = await WatchedPositionAsync(client, token);

        using (var accepted = await Wire.SimulateAlertAsync(client, token, second))
        {
            accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await Wire.Describe(accepted));
        }

        var row = (await Wire.ListFiredAlertsAsync(client, token)).ShouldHaveSingleItem();

        row.Ticker.ShouldBe(second);
        row.Ticker.ShouldNotBe(first);
    }

    [Fact]
    public async Task Simulating_ATickerWithNoThreshold_IsRejected()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "simulate-unwatched");

        _ = await WatchedPositionAsync(client, token);

        using var response = await Wire.SimulateAlertAsync(client, token, Wire.UniqueTicker());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await Wire.Describe(response));
    }

    [Fact]
    public async Task Simulating_AMalformedTicker_IsRejected_NamingTheField()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "simulate-shape");

        using var response = await Wire.SimulateAlertAsync(client, token, "BRK.B");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Wire.Describe(response));

        (await Wire.FailingFieldsAsync(response)).ShouldContain("ticker");
    }

    private static async Task<string> SignedInAsync(HttpClient client, string prefix) =>
        (await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail(prefix))).AccessToken;

    private static async Task<string> WatchedPositionAsync(HttpClient client, string accessToken)
    {
        var ticker = Wire.UniqueTicker();

        using (var bought = await Wire.AddHoldingAsync(client, accessToken, ticker, 10m, 100m))
        {
            bought.StatusCode.ShouldBe(HttpStatusCode.Created, await Wire.Describe(bought));
        }

        using var saved = await Wire.SaveAlertSettingAsync(client, accessToken, ticker, 5m, 30);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(saved));

        return ticker;
    }
}
