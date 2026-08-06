using System.Globalization;
using System.Net;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>A threshold belongs to a position: you must hold it, and there is one per ticker.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class AlertSettingsTests(ApiFixture fixture)
{
    /// <summary>Matches Alerts:MaxWindowMinutes in appsettings.json.</summary>
    private const int MaxWindowMinutes = 60;

    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>An empty list, not a 404: the portfolio page reads this on every mount.</summary>
    [Fact]
    public async Task Settings_ForAUserWithNone_AreAnEmptyList()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "alerts-empty");

        (await Wire.ListAlertSettingsAsync(client, token)).ShouldBeEmpty();
    }

    /// <summary>A threshold on something you do not own is refused by state, so 409 rather than 400.</summary>
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

    /// <summary>The cap is configuration, so the message has to carry both numbers to be actionable.</summary>
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

    /// <summary>The cap's edge is accepted, so the rule above is a boundary and not an off-by-one.</summary>
    [Fact]
    public async Task Saving_AWindowExactlyAtTheCap_IsAccepted()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "alerts-window-edge");

        var ticker = await OpenPositionAsync(client, token);

        using var response = await Wire.SaveAlertSettingAsync(client, token, ticker, 5m, MaxWindowMinutes);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));
    }

    /// <summary>Lower case in, canonical out — and the round trip through GET returns the same row.</summary>
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

    /// <summary>Saving twice updates the row rather than adding a second — the unique index proves it.</summary>
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

    /// <summary>One user's thresholds are their own, which is the only thing keeping them private.</summary>
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

    /// <summary>Shape is the filter's job and reaches no handler, so it is a 400 naming the field.</summary>
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

    /// <summary>Opens a position on a fresh symbol and returns it, so a threshold has something to sit on.</summary>
    private static async Task<string> OpenPositionAsync(HttpClient client, string accessToken)
    {
        var ticker = Wire.UniqueTicker();

        using var response = await Wire.AddHoldingAsync(client, accessToken, ticker, 10m, 100m);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await Wire.Describe(response));

        return ticker;
    }
}
