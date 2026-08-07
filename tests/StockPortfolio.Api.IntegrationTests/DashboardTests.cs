using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Api.IntegrationTests.Infrastructure;
using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Api.IntegrationTests;

[Collection(ApiCollectionDefinition.Name)]
public sealed class DashboardTests(ApiFixture fixture)
{
    // Nothing the generated provider produces lands here, so "fresh" and "last known" cannot be confused.
    private const decimal ScriptedPrice = 4242.4242m;

    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public async Task Dashboard_WithHoldingsAndPrices_ReturnsJoinedTotals()
    {
        var (client, token) = await SignedInAsync("dashboard-totals");

        var first = Wire.UniqueTicker();
        var second = Wire.UniqueTicker();

        await AddSucceedsAsync(client, token, first, 10m, 100m);
        await AddSucceedsAsync(client, token, second, 4m, 25m);

        var dashboard = await Wire.GetDashboardAsync(client, token);

        dashboard.Positions.Count.ShouldBe(2);
        dashboard.Totals.PositionCount.ShouldBe(2);
        dashboard.Totals.PricedPositionCount.ShouldBe(
            2,
            "both symbols were fetched on this very request, so neither can be unpriced");

        foreach (var position in dashboard.Positions)
        {
            position.CurrentPrice.ShouldNotBeNull($"{position.Ticker} came back without a price");
            position.MarketValue.ShouldNotBeNull();
            position.Profit.ShouldNotBeNull();
            position.IsLastKnown.ShouldBeFalse("nothing fell back; the provider answered for both");

            Amount(position.MarketValue).ShouldBe(
                Amount(position.CurrentPrice) * position.Quantity,
                $"{position.Ticker}'s market value is not price times quantity");

            Amount(position.Profit).ShouldBe(
                Amount(position.MarketValue) - Amount(position.Cost),
                $"{position.Ticker}'s profit is not value minus cost");
        }

        Amount(dashboard.Totals.Cost).ShouldBe(1100m);

        Amount(dashboard.Totals.Value).ShouldBe(
            dashboard.Positions.Sum(position => Amount(position.MarketValue!)),
            "the total is not the sum of the rows it is displayed above");

        Amount(dashboard.Totals.Profit)
            .ShouldBe(Amount(dashboard.Totals.Value) - Amount(dashboard.Totals.Cost));

        dashboard.Positions
            .Sum(position => decimal.Parse(position.Weight!, CultureInfo.InvariantCulture))
            .ShouldBe(100m, 2 * 0.005m, "the priced weights do not account for the whole portfolio");

        dashboard.StalestObservedAt.ShouldNotBeNull("every position is priced, so min(observedAt) exists");
    }

    [Fact]
    public async Task Dashboard_SerialisesWeightAndProfitPercentAsStrings_NotJsonNumbers()
    {
        var (client, token) = await SignedInAsync("dashboard-percent-shape");

        await AddSucceedsAsync(client, token, Wire.UniqueTicker(), 3m, 40m);

        // Read as text, not as an object: a deserialiser turns a JSON number back into the string property, so only the raw body says which crossed the wire.
        var body = await Wire.GetDashboardJsonAsync(client, token);

        body.ShouldContain(
            "\"weight\":\"",
            Case.Sensitive,
            "weight arrived as a JSON number, so JSON.parse will make it a double — the identical trap "
            + $"MoneyJsonConverter exists to avoid for money: {body}");

        body.ShouldContain(
            "\"profitPercent\":\"",
            Case.Sensitive,
            $"profitPercent arrived as a JSON number: {body}");
    }

    [Fact]
    public async Task Dashboard_NewlyAddedTicker_HasPriceOnFirstRequest()
    {
        var (client, token) = await SignedInAsync("dashboard-new-ticker");

        var ticker = Wire.UniqueTicker();
        var before = TimeProvider.System.GetUtcNow();

        await AddSucceedsAsync(client, token, ticker, 7m, 12m);

        var dashboard = await Wire.GetDashboardAsync(client, token);

        var only = dashboard.Positions.ShouldHaveSingleItem();

        only.Ticker.ShouldBe(ticker);
        only.CurrentPrice.ShouldNotBeNull(
            "a symbol added seconds ago has no history anywhere, so a price here can only have come "
            + "from this request fetching it");

        only.IsLastKnown.ShouldBeFalse("nothing had ever written marketdata:last: for this symbol");

        only.ObservedAt.ShouldNotBeNull();
        only.ObservedAt.Value.ShouldBeGreaterThan(
            before,
            "the observation predates the position itself, so it cannot have been fetched for it");
    }

    [Fact]
    public async Task Dashboard_ProviderDown_ShowsLastKnownWithAge()
    {
        var (client, token) = await SignedInAsync("dashboard-provider-down");

        var ticker = Wire.UniqueTicker();

        await AddSucceedsAsync(client, token, ticker, 2m, 50m);

        // Warmed on the shared host: two hosts, one Redis container, which is the only reason the dead-provider host below has a fallback.
        var warm = await Wire.GetDashboardAsync(client, token);
        var warmed = warm.Positions.ShouldHaveSingleItem();

        warmed.CurrentPrice.ShouldNotBeNull("nothing was warmed, so the rest of this test proves nothing");

        await using var host = _fixture.CreateHostWithQuoteProvider(ScriptedQuoteProvider.ServingNothing);
        using var deadClient = host.CreateClient();

        var degraded = await Wire.GetDashboardAsync(deadClient, token);
        var position = degraded.Positions.ShouldHaveSingleItem();

        position.CurrentPrice.ShouldNotBeNull(
            "the provider is down and marketdata:last: holds this symbol — falling to null here is the "
            + "fallback not being read at all");

        Amount(position.CurrentPrice).ShouldBe(
            Amount(warmed.CurrentPrice),
            "the price served back is not the one that was recorded");

        position.IsLastKnown.ShouldBeTrue(
            "a stale price rendered as fresh is worse than no price: the amber state is driven by this flag");

        position.ObservedAt.ShouldNotBeNull();
        position.ObservedAt.Value.ShouldBe(
            warmed.ObservedAt!.Value,
            TimeSpan.FromMilliseconds(1),
            "the age shown is not the age of the observation (the key stores epoch milliseconds)");

        position.ObservedAt.Value.ShouldBeLessThan(
            degraded.AsOf,
            "the observation is not older than the request, so no age is being reported at all");

        degraded.Totals.PricedPositionCount.ShouldBe(1);
    }

    [Fact]
    public async Task Dashboard_ProviderDown_NeverFetchedTicker_ReturnsNullNotZero()
    {
        await using var host = _fixture.CreateHostWithQuoteProvider(ScriptedQuoteProvider.ServingNothing);
        using var client = host.CreateClient();

        var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail("dashboard-never-fetched"));
        var ticker = Wire.UniqueTicker();

        await AddSucceedsAsync(client, tokens.AccessToken, ticker, 5m, 20m);

        var dashboard = await Wire.GetDashboardAsync(client, tokens.AccessToken);
        var only = dashboard.Positions.ShouldHaveSingleItem();

        only.Ticker.ShouldBe(ticker, "the position must still be listed, just without a price");
        only.CurrentPrice.ShouldBeNull();
        only.MarketValue.ShouldBeNull();
        only.Profit.ShouldBeNull();
        only.ProfitPercent.ShouldBeNull();
        only.ObservedAt.ShouldBeNull();
        only.IsLastKnown.ShouldBeFalse("there is no last-known price to have fallen back to");

        only.Weight.ShouldBeNull(
            "a zero weight claims this is 0% of the portfolio; the truth is that nobody knows");

        Amount(only.Cost).ShouldBe(100m);

        dashboard.Totals.PositionCount.ShouldBe(1);
        dashboard.Totals.PricedPositionCount.ShouldBe(0);
        Amount(dashboard.Totals.Value).ShouldBe(0m);

        Amount(dashboard.Totals.Cost).ShouldBe(
            0m,
            "Cost is summed over the same subset as Value. Including an unpriced position's cost here "
            + "reports a loss the size of that position on a portfolio nobody can value at all.");

        dashboard.StalestObservedAt.ShouldBeNull("no position is priced, so there is no min(observedAt)");

        dashboard.Totals.ProfitPercent.ShouldBeNull(
            "\"0.00\" tells the holder their portfolio is exactly break-even at the moment nothing "
            + "about it could be priced — the claim Weight already refuses to make one level down");

        // Null, not absent: DefaultIgnoreCondition = WhenWritingNull drops nullable value types too, so without JsonIgnore(Never) the member never reaches the wire.
        var body = await Wire.GetDashboardJsonAsync(client, tokens.AccessToken);

        body.ShouldContain("\"currentPrice\":null", Case.Sensitive, $"currentPrice is absent: {body}");
        body.ShouldContain("\"weight\":null", Case.Sensitive, $"weight is absent: {body}");
        body.ShouldContain("\"observedAt\":null", Case.Sensitive, $"observedAt is absent: {body}");
        body.ShouldContain(
            "\"stalestObservedAt\":null",
            Case.Sensitive,
            $"stalestObservedAt is absent: {body}");

        // Navigated rather than string-matched: the unpriced row also carries "profitPercent":null, so a substring search would pass with the totals member missing.
        using var document = JsonDocument.Parse(body);

        document.RootElement.GetProperty("totals")
            .TryGetProperty("profitPercent", out var percent)
            .ShouldBeTrue($"totals.profitPercent is absent rather than null: {body}");

        percent.ValueKind.ShouldBe(JsonValueKind.Null, $"totals.profitPercent is not null: {body}");
    }

    [Fact]
    public async Task Dashboard_HostWithQuoteProvider_ResolvesNoQuoteNudge()
    {
        await using var host = _fixture.CreateHostWithQuoteProvider(ScriptedQuoteProvider.ServingNothing);

        // White-box on purpose: under EnvironmentName "Testing" the nudge route is never mapped, so deleting RemoveAll<IQuoteNudge>() would fail nothing here.
        host.Services.GetService<IQuoteNudge>().ShouldBeNull(
            "the swapped host still resolves an IQuoteNudge, which can only be the registration made "
            + "against the fake provider that CreateHostWithQuoteProvider removed");
    }

    [Fact]
    public async Task Dashboard_ForAUserWithTheirOwnKey_UsesThatKey()
    {
        const string OwnKey = "d1v3rs3-k3y-a1b2";

        var ticker = Wire.UniqueTicker();
        var provider = ScriptedQuoteProvider.Serving((ticker, ScriptedPrice));

        await using var host = _fixture.CreateHostWithQuoteProvider(provider);
        using var client = host.CreateClient();

        var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail("dashboard-byok"));

        using var saved = await Wire.SaveApiKeyAsync(client, tokens.AccessToken, OwnKey);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(saved));

        await AddSucceedsAsync(client, tokens.AccessToken, ticker, 1m, 10m);

        var dashboard = await Wire.GetDashboardAsync(client, tokens.AccessToken);

        Amount(Position(dashboard, ticker).CurrentPrice!).ShouldBe(ScriptedPrice);

        provider.ApiKeyOverridesSeen.ShouldContain(
            OwnKey,
            "the dashboard fetch never carried the caller's own key to the provider");
    }

    [Fact]
    public async Task Dashboard_ProviderOmitsSomeSymbols_Returns200NotError()
    {
        // The double omits the refused symbol rather than emitting a 429: IQuoteProvider fetches what it can, so what reaches this layer is a shorter list.
        var served = Wire.UniqueTicker();
        var alsoServed = Wire.UniqueTicker();

        // Three positions, one refused. Failing all three would not distinguish the per-ticker set difference from one try/catch around the whole call.
        var refused = Wire.UniqueTicker();

        await using var host = _fixture.CreateHostWithQuoteProvider(
            ScriptedQuoteProvider.Serving((served, ScriptedPrice), (alsoServed, ScriptedPrice)));

        using var client = host.CreateClient();

        var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail("dashboard-429"));

        await AddSucceedsAsync(client, tokens.AccessToken, served, 1m, 10m);
        await AddSucceedsAsync(client, tokens.AccessToken, alsoServed, 1m, 10m);
        await AddSucceedsAsync(client, tokens.AccessToken, refused, 1m, 10m);

        using var response = await Wire.SendAsync(
            client,
            HttpMethod.Get,
            Wire.DashboardPath,
            tokens.AccessToken);

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "every row of the failure matrix returns 200; a rate-limited symbol degrades the table, it "
            + $"does not fail the request: {await Wire.Describe(response)}");

        var dashboard = await response.Content.ReadFromJsonAsync<DashboardPayload>(
            JsonSerializerOptions.Web,
            TestContext.Current.CancellationToken);

        dashboard.ShouldNotBeNull();

        dashboard.Totals.PricedPositionCount.ShouldBe(
            2,
            "one symbol was refused and two were served; discarding all three is the implementation "
            + "§2.5 rejects, and pricing all three means the refusal never happened");

        Amount(Position(dashboard, served).CurrentPrice!).ShouldBe(ScriptedPrice);
        Amount(Position(dashboard, alsoServed).CurrentPrice!).ShouldBe(ScriptedPrice);
        Position(dashboard, refused).CurrentPrice.ShouldBeNull();

        // The flag, not just the number: re-reading the two good prices out of the store this request just wrote gives the identical figures with IsLastKnown set.
        Position(dashboard, served).IsLastKnown.ShouldBeFalse(
            "this symbol was answered by the provider on this request, so its price is not a fallback");

        Position(dashboard, alsoServed).IsLastKnown.ShouldBeFalse();
    }

    // Seventeen good prices are not thrown away because three failed.
    [Fact]
    public async Task Dashboard_PartialProviderFailure_MixesFreshAndLastKnown()
    {
        var (client, token) = await SignedInAsync("dashboard-partial");

        var fresh = Wire.UniqueTicker();
        var stale = Wire.UniqueTicker();

        await AddSucceedsAsync(client, token, fresh, 1m, 10m);
        await AddSucceedsAsync(client, token, stale, 1m, 10m);

        // Warm both on the shared host, so a wholly-stale answer IS available and is wrong — which is what makes the assertion below discriminating.
        var warm = await Wire.GetDashboardAsync(client, token);
        var warmedStale = Position(warm, stale);

        warmedStale.CurrentPrice.ShouldNotBeNull();
        Position(warm, fresh).CurrentPrice.ShouldNotBeNull();

        await using var host = _fixture.CreateHostWithQuoteProvider(
            ScriptedQuoteProvider.Serving((fresh, ScriptedPrice)));

        using var partialClient = host.CreateClient();

        var dashboard = await Wire.GetDashboardAsync(partialClient, token);

        var freshPosition = Position(dashboard, fresh);
        var stalePosition = Position(dashboard, stale);

        freshPosition.CurrentPrice.ShouldNotBeNull();
        Amount(freshPosition.CurrentPrice).ShouldBe(
            ScriptedPrice,
            "the symbol the provider DID answer for came back with its stored value instead — one "
            + "try/catch around the whole provider call discards good prices because a sibling failed");

        freshPosition.IsLastKnown.ShouldBeFalse();

        stalePosition.CurrentPrice.ShouldNotBeNull(
            "the failing symbol fell to nothing at all rather than to its last known price");

        Amount(stalePosition.CurrentPrice).ShouldBe(Amount(warmedStale.CurrentPrice));
        stalePosition.IsLastKnown.ShouldBeTrue();

        dashboard.Totals.PricedPositionCount.ShouldBe(2, "both rows carry a price, from two sources");
    }

    [Fact]
    public async Task Dashboard_RedisDown_StillReturnsFreshPrices()
    {
        await using var host = _fixture.CreateHostWithRedisDown();
        using var client = host.CreateClient();

        var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail("dashboard-redis-down"));
        var ticker = Wire.UniqueTicker();

        await AddSucceedsAsync(client, tokens.AccessToken, ticker, 3m, 30m);

        var dashboard = await Wire.GetDashboardAsync(client, tokens.AccessToken);
        var only = dashboard.Positions.ShouldHaveSingleItem();

        only.CurrentPrice.ShouldNotBeNull(
            "the provider answered and the caller is waiting on prices; a failed best-effort write to "
            + "the fallback store must never be able to take the request with it");

        only.IsLastKnown.ShouldBeFalse("this price came from the provider, not from a store that is down");
        dashboard.Totals.PricedPositionCount.ShouldBe(1);

        using var ready = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        ready.StatusCode.ShouldBe(
            HttpStatusCode.ServiceUnavailable,
            "readiness reported healthy with Redis unreachable, so the 200 above says nothing about "
            + $"degradation — it would look identical to a healthy instance: {await Wire.Describe(ready)}");
    }

    [Fact]
    public async Task Dashboard_OnlyReturnsCallersHoldings()
    {
        var (aliceClient, aliceToken) = await SignedInAsync("dashboard-alice");

        var ticker = Wire.UniqueTicker();

        await AddSucceedsAsync(aliceClient, aliceToken, ticker, 2m, 100m);

        var (bobClient, bobToken) = await SignedInAsync("dashboard-bob");

        var bobs = await Wire.GetDashboardAsync(bobClient, bobToken);

        bobs.Positions.ShouldBeEmpty("Bob is looking at Alice's portfolio");
        bobs.Totals.PositionCount.ShouldBe(0);
        Amount(bobs.Totals.Value).ShouldBe(0m);

        // Alice still sees hers, so Bob's emptiness is scoping rather than a broken read.
        var alices = await Wire.GetDashboardAsync(aliceClient, aliceToken);

        alices.Positions.ShouldHaveSingleItem().Ticker.ShouldBe(ticker);
    }

    [Fact]
    public async Task Dashboard_GeneratedSql_UsesParameterPlaceholder()
    {
        // The shared host deliberately: RecordedCommands is passed only to that one, so a read on any host above would record nothing and pass by finding nothing.
        var (client, token) = await SignedInAsync("dashboard-parameterised");

        var ticker = Wire.UniqueTicker();

        await AddSucceedsAsync(client, token, ticker, 1m, 10m);

        var userId = (await SubjectOfAsync(client, token)).ToString();
        var before = _fixture.RecordedCommands.Commands.Count;

        await Wire.GetDashboardAsync(client, token);

        var reads = _fixture.RecordedCommands.Commands
            .Skip(before)
            .Where(command => command.CommandText.Contains(
                "portfolio.holdings",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        reads.ShouldNotBeEmpty(
            "the dashboard request produced no statement against portfolio.holdings, so there is "
            + "nothing here to have proved anything about");

        reads.ShouldContain(
            command => command.ParameterValues.Any(
                value => value.Contains(userId, StringComparison.OrdinalIgnoreCase)),
            "the caller's id never travelled as a parameter value, so nothing was proved about how it "
            + "would have been sent");

        foreach (var command in reads)
        {
            command.CommandText.ShouldNotContain(
                userId,
                Case.Insensitive,
                $"the caller's id was concatenated into SQL: {command.CommandText}");

            command.CommandText.ShouldNotContain(
                ticker,
                Case.Insensitive,
                $"a user-supplied ticker was concatenated into SQL: {command.CommandText}");

            command.Parameters.ShouldNotBeEmpty(
                $"a dashboard read carrying no parameters at all: {command.CommandText}");

            foreach (var parameter in command.Parameters)
            {
                command.CommandText.ShouldContain(
                    parameter.Name,
                    Case.Sensitive,
                    $"parameter '{parameter.Name}' is not referenced from the statement text, so the "
                    + $"value it carries may have been inlined instead: {command.CommandText}");
            }
        }
    }

    private static decimal Amount(MoneyPayload payload) =>
        decimal.Parse(payload.Amount, CultureInfo.InvariantCulture);

    private static DashboardPositionPayload Position(DashboardPayload dashboard, string ticker) =>
        dashboard.Positions.SingleOrDefault(
            position => string.Equals(position.Ticker, ticker, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"The dashboard carries no row for {ticker}; it has "
            + $"[{string.Join(", ", dashboard.Positions.Select(position => position.Ticker))}].");

    private static async Task<Guid> SubjectOfAsync(HttpClient client, string accessToken)
    {
        using var response = await Wire.SendAsync(client, HttpMethod.Get, "/api/auth/me", accessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        var user = await response.Content.ReadFromJsonAsync<UserPayload>(JsonSerializerOptions.Web);

        user.ShouldNotBeNull();

        return user.Id;
    }

    private static async Task AddSucceedsAsync(
        HttpClient client,
        string accessToken,
        string ticker,
        decimal quantity,
        decimal price)
    {
        using var response = await Wire.AddHoldingAsync(client, accessToken, ticker, quantity, price);

        response.IsSuccessStatusCode.ShouldBeTrue(await Wire.Describe(response));
    }

    private async Task<(HttpClient Client, string Token)> SignedInAsync(string prefix)
    {
        var client = _fixture.CreateClient();
        var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail(prefix));

        return (client, tokens.AccessToken);
    }
}
