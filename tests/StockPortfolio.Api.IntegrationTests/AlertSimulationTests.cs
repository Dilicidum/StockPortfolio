using System.Net;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>The manual trigger. It writes a real row, which is what makes it worth having.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class AlertSimulationTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>Simulate, then read history back — which is also "simulate with the tab closed".</summary>
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

    /// <summary>Nothing to simulate is a 409 naming the fix, not a 500 and not an invented alert.</summary>
    [Fact]
    public async Task Simulating_WithNoThreshold_IsRejected()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "simulate-nothing");

        using var response = await Wire.SimulateAlertAsync(client, token);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await Wire.Describe(response));

        (await Wire.ListFiredAlertsAsync(client, token)).ShouldBeEmpty();
    }

    /// <summary>A named ticker the caller watches is the one that fires.</summary>
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

    /// <summary>A named ticker with no threshold says so rather than firing a different position.</summary>
    [Fact]
    public async Task Simulating_ATickerWithNoThreshold_IsRejected()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "simulate-unwatched");

        _ = await WatchedPositionAsync(client, token);

        using var response = await Wire.SimulateAlertAsync(client, token, Wire.UniqueTicker());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await Wire.Describe(response));
    }

    /// <summary>Something that is not a ticker at all is shape, so the filter answers 400 naming the field.</summary>
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

    /// <summary>Opens a position and sets a five percent threshold on it, which is what Simulate needs.</summary>
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
