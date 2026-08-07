using System.Globalization;
using System.Net;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

[Collection(ApiCollectionDefinition.Name)]
public sealed class AlertSettingsTests(ApiFixture fixture)
{
    // Matches Alerts:MaxWindowMinutes in appsettings.json.
    private const int MaxWindowMinutes = 60;

    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public async Task Settings_ForAUserWithNone_AreAnEmptyList()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "alerts-empty");

        (await Wire.ListAlertSettingsAsync(client, token)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Saving_AThresholdOnATickerYouDoNotHold_IsRejected()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "alerts-not-held");

        var ticker = Wire.UniqueTicker();

        using var response = await Wire.SaveAlertSettingAsync(client, token, ticker, 5m, 30);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await Wire.Describe(response));
        (await Wire.Describe(response)).ShouldContain(ticker);

        (await Wire.ListAlertSettingsAsync(client, token)).ShouldBeEmpty(
            "a refused save must not leave a row behind.");
    }

    [Fact]
    public async Task Saving_AWindowOverTheCap_IsRejected_NamingBothNumbers()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "alerts-window");

        var ticker = await OpenPositionAsync(client, token);

        using var response = await Wire.SaveAlertSettingAsync(
            client,
            token,
            ticker,
            5m,
            MaxWindowMinutes + 1);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await Wire.Describe(response));

        var body = await Wire.Describe(response);

        body.ShouldContain((MaxWindowMinutes + 1).ToString(CultureInfo.InvariantCulture));
        body.ShouldContain(MaxWindowMinutes.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Saving_AWindowExactlyAtTheCap_IsAccepted()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "alerts-window-edge");

        var ticker = await OpenPositionAsync(client, token);

        using var response = await Wire.SaveAlertSettingAsync(client, token, ticker, 5m, MaxWindowMinutes);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));
    }

    [Fact]
    public async Task AValidThreshold_RoundTripsThroughGet_Canonicalised()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "alerts-round-trip");

        var ticker = await OpenPositionAsync(client, token);

        using var saved = await Wire.SaveAlertSettingAsync(
            client,
            token,
            ticker.ToLowerInvariant(),
            7.5m,
            15);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(saved));

        var listed = (await Wire.ListAlertSettingsAsync(client, token)).ShouldHaveSingleItem();

        listed.Ticker.ShouldBe(ticker);
        listed.ThresholdPercent.ShouldBe(7.5m);
        listed.WindowMinutes.ShouldBe(15);
        listed.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task SavingTwice_ReplacesTheThreshold_RatherThanDuplicatingIt()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "alerts-twice");

        var ticker = await OpenPositionAsync(client, token);

        using (var first = await Wire.SaveAlertSettingAsync(client, token, ticker, 3m, 10))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(first));
        }

        using (var second = await Wire.SaveAlertSettingAsync(client, token, ticker, 9m, 45, enabled: false))
        {
            second.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(second));
        }

        var listed = (await Wire.ListAlertSettingsAsync(client, token)).ShouldHaveSingleItem(
            "the unique index on (user_id, ticker) means the second save is an update, not an insert.");

        listed.ThresholdPercent.ShouldBe(9m);
        listed.WindowMinutes.ShouldBe(45);
        listed.Enabled.ShouldBeFalse(
            "switching a threshold off must survive the round trip. A store default of true with the "
                + "wrong sentinel would silently write it back on.");
    }

    [Fact]
    public async Task Settings_AreScopedToTheCaller()
    {
        using var client = _fixture.CreateClient();

        var mine = await SignedInAsync(client, "alerts-mine");
        var ticker = await OpenPositionAsync(client, mine);

        using (var saved = await Wire.SaveAlertSettingAsync(client, mine, ticker, 4m, 20))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(saved));
        }

        var stranger = await SignedInAsync(client, "alerts-stranger");

        (await Wire.ListAlertSettingsAsync(client, stranger)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("BRK.B", 5, 30, "ticker")]
    [InlineData("AAPL", 0, 30, "thresholdPercent")]
    [InlineData("AAPL", 101, 30, "thresholdPercent")]
    [InlineData("AAPL", 5, 0, "windowMinutes")]
    public async Task AMalformedThreshold_IsRejected_NamingTheField(
        string ticker,
        decimal percent,
        int window,
        string field)
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "alerts-shape");

        using var response = await Wire.SaveAlertSettingAsync(client, token, ticker, percent, window);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Wire.Describe(response));

        (await Wire.FailingFieldsAsync(response)).ShouldContain(field);
    }

    private static async Task<string> SignedInAsync(HttpClient client, string prefix) =>
        (await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail(prefix))).AccessToken;

    private static async Task<string> OpenPositionAsync(HttpClient client, string accessToken)
    {
        var ticker = Wire.UniqueTicker();

        using var response = await Wire.AddHoldingAsync(client, accessToken, ticker, 10m, 100m);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await Wire.Describe(response));

        return ticker;
    }
}
