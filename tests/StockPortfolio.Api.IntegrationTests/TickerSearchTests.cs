using System.Net;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

[Collection(ApiCollectionDefinition.Name)]
public sealed class TickerSearchTests(ApiFixture fixture)
{
    // The name the keyless catalogue gives AAPL; the suite never runs against the live provider.
    private const string AppleName = "Apple Inc";

    private const string MicrosoftName = "Microsoft Corporation";

    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public async Task Search_Anonymous_Is401()
    {
        using var client = _fixture.CreateClient();

        using var response = await Wire.SearchTickersAsync(client, accessToken: null, "appl");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }

    [Fact]
    public async Task Search_KnownPrefix_ListsTheSymbolAndItsCompanyName()
    {
        var (client, token) = await SignedInAsync("search-known");

        var matches = await Wire.SearchSucceedsAsync(client, token, "appl");

        var apple = matches.SingleOrDefault(match => match.Symbol == "AAPL");

        apple.ShouldNotBeNull(
            "the keyless catalogue did not answer for 'appl', so a clean clone loses the feature "
            + $"entirely: [{string.Join(", ", matches.Select(match => match.Symbol))}]");

        apple.Description.ShouldBe(AppleName);
    }

    [Fact]
    public async Task Search_PartialSymbol_IsAllowedToMatchMoreThanTheSymbolItself()
    {
        var (client, token) = await SignedInAsync("search-fuzzy");

        var matches = await Wire.SearchSucceedsAsync(client, token, "aap");

        matches.Select(match => match.Symbol).ShouldContain(
            "AAPL",
            "q=AAP must still find AAPL — reusing the existence check's exact match here would return "
            + "nothing for every query that is not already the answer");
    }

    [Fact]
    public async Task Search_EverySuggestion_CanBeAdded()
    {
        var (client, token) = await SignedInAsync("search-addable");

        var matches = await Wire.SearchSucceedsAsync(client, token, "corp");

        matches.ShouldNotBeEmpty("nothing matched, so this assertion checks nothing");

        foreach (var match in matches)
        {
            using var response = await Wire.AddHoldingAsync(client, token, match.Symbol, 1m, 1m);

            response.IsSuccessStatusCode.ShouldBeTrue(
                $"'{match.Symbol}' was offered as a suggestion and the form rejects it: "
                + await Wire.Describe(response));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    public async Task Search_EmptyOrTooShortQuery_Is200AndEmpty(string query)
    {
        var (client, token) = await SignedInAsync("search-short");

        (await Wire.SearchSucceedsAsync(client, token, query)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_QueryParameterMissing_Is200AndEmpty()
    {
        var (client, token) = await SignedInAsync("search-no-q");

        using var response = await Wire.SendAsync(client, HttpMethod.Get, Wire.SearchPath, token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Trim()
            .ShouldBe("[]");
    }

    [Fact]
    public async Task Search_ProviderCannotAnswer_Is200AndEmptyNeverAnError()
    {
        await using var host = _fixture.CreateHostWithQuoteProvider(ScriptedQuoteProvider.ServingNothing);
        using var client = host.CreateClient();

        var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail("search-provider-down"));

        using var response = await Wire.SearchTickersAsync(client, tokens.AccessToken, "appl");

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "a provider that cannot answer must degrade the field to a plain text box, not fail the "
            + $"request the add-position form sits behind: {await Wire.Describe(response)}");

        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Trim()
            .ShouldBe("[]");

        using var added = await Wire.AddHoldingAsync(client, tokens.AccessToken, "AAPL", 1m, 10m);

        added.IsSuccessStatusCode.ShouldBeTrue(await Wire.Describe(added));
    }

    [Fact]
    public async Task Search_ResponseIsABareArray()
    {
        var (client, token) = await SignedInAsync("search-shape");

        using var response = await Wire.SearchTickersAsync(client, token, "appl");

        var body = (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Trim();

        body.ShouldStartWith("[", Case.Sensitive, $"the search response is not a bare array: {body}");
        body.ShouldContain("\"symbol\":", Case.Sensitive, body);
        body.ShouldContain("\"description\":", Case.Sensitive, body);
    }

    [Fact]
    public async Task Holdings_AfterASearch_CarryTheCompanyName()
    {
        var (client, token) = await SignedInAsync("names-holdings");

        // Warm two names, so a swapped pair below is a visible failure rather than a coincidence.
        await WarmAsync(client, token, "aapl", "AAPL");
        await WarmAsync(client, token, "msft", "MSFT");

        var unnamed = Wire.UniqueTicker();

        await AddSucceedsAsync(client, token, "AAPL");
        await AddSucceedsAsync(client, token, unnamed);
        await AddSucceedsAsync(client, token, "MSFT");

        var holdings = await Wire.ListHoldingsAsync(client, token);

        Holding(holdings, "AAPL").Name.ShouldBe(AppleName);
        Holding(holdings, "MSFT").Name.ShouldBe(MicrosoftName);

        // A batched read that lost its alignment would hand one of these names to this row instead.
        Holding(holdings, unnamed).Name.ShouldBeNull(
            "a symbol nobody has ever searched for has no cached name, and the row still lists");
    }

    [Fact]
    public async Task Holdings_WithNoCachedName_SerialiseNameAsAnExplicitNull()
    {
        var (client, token) = await SignedInAsync("names-holdings-null");

        await AddSucceedsAsync(client, token, Wire.UniqueTicker());

        var body = await Wire.ListHoldingsJsonAsync(client, token);

        body.ShouldContain(
            "\"name\":null",
            Case.Sensitive,
            "name is absent rather than null. Program.cs sets DefaultIgnoreCondition to WhenWritingNull, "
            + $"so the member needs JsonIgnore(Never) to reach the wire at all: {body}");
    }

    [Fact]
    public async Task Dashboard_AfterASearch_CarriesTheCompanyName()
    {
        var (client, token) = await SignedInAsync("names-dashboard");

        await WarmAsync(client, token, "aapl", "AAPL");

        var unnamed = Wire.UniqueTicker();

        await AddSucceedsAsync(client, token, "AAPL");
        await AddSucceedsAsync(client, token, unnamed);

        var dashboard = await Wire.GetDashboardAsync(client, token);

        Position(dashboard, "AAPL").Name.ShouldBe(AppleName);
        Position(dashboard, unnamed).Name.ShouldBeNull();

        var body = await Wire.GetDashboardJsonAsync(client, token);

        body.ShouldContain("\"name\":null", Case.Sensitive, $"a row's name is absent rather than null: {body}");
    }

    [Fact]
    public async Task Holdings_ProviderDown_StillCarryTheirCachedNames()
    {
        var (client, token) = await SignedInAsync("names-provider-down");

        // Warmed on the shared host: two hosts, one Redis container, which is the only reason the dead-provider host below finds a name.
        await WarmAsync(client, token, "aapl", "AAPL");

        await using var host = _fixture.CreateHostWithQuoteProvider(ScriptedQuoteProvider.ServingNothing);
        using var deadClient = host.CreateClient();

        await AddSucceedsAsync(deadClient, token, "AAPL");

        var holdings = await Wire.ListHoldingsAsync(deadClient, token);

        Holding(holdings, "AAPL").Name.ShouldBe(
            AppleName,
            "the name came back empty with the provider down, so this page is reading it from the "
            + "provider rather than from the cache — which is the dependency it exists without");
    }

    [Fact]
    public async Task Holdings_RedisDown_StillListEveryPositionWithoutNames()
    {
        await using var host = _fixture.CreateHostWithRedisDown();
        using var client = host.CreateClient();

        var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail("names-redis-down"));

        await AddSucceedsAsync(client, tokens.AccessToken, "AAPL");

        var holdings = await Wire.ListHoldingsAsync(client, tokens.AccessToken);
        var only = holdings.ShouldHaveSingleItem();

        only.Ticker.ShouldBe("AAPL");
        only.Name.ShouldBeNull("a name store that cannot answer means no name, never a failed page");

        // Everything else about the row is intact: quantity, price and cost never went near Redis.
        only.Quantity.ShouldBe(1m);
    }

    [Fact]
    public async Task Search_RepeatedQuery_StillAnswers()
    {
        var (client, token) = await SignedInAsync("search-repeat");

        var first = await Wire.SearchSucceedsAsync(client, token, "appl");
        var second = await Wire.SearchSucceedsAsync(client, token, "appl");

        second.Select(match => match.Symbol).ShouldBe(first.Select(match => match.Symbol));
    }

    private static async Task WarmAsync(HttpClient client, string token, string query, string expected)
    {
        var matches = await Wire.SearchSucceedsAsync(client, token, query);

        matches.Select(match => match.Symbol).ShouldContain(
            expected,
            $"'{query}' did not match {expected}, so no name was cached and the assertions that follow "
            + "would be checking an empty cache");
    }

    private static async Task AddSucceedsAsync(HttpClient client, string accessToken, string ticker)
    {
        using var response = await Wire.AddHoldingAsync(client, accessToken, ticker, 1m, 10m);

        response.IsSuccessStatusCode.ShouldBeTrue(await Wire.Describe(response));
    }

    private static HoldingPayload Holding(IReadOnlyList<HoldingPayload> holdings, string ticker) =>
        holdings.SingleOrDefault(holding => string.Equals(holding.Ticker, ticker, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"No position for {ticker}; the list has "
            + $"[{string.Join(", ", holdings.Select(holding => holding.Ticker))}].");

    private static DashboardPositionPayload Position(DashboardPayload dashboard, string ticker) =>
        dashboard.Positions.SingleOrDefault(
            position => string.Equals(position.Ticker, ticker, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"The dashboard carries no row for {ticker}; it has "
            + $"[{string.Join(", ", dashboard.Positions.Select(position => position.Ticker))}].");

    private async Task<(HttpClient Client, string Token)> SignedInAsync(string prefix)
    {
        var client = _fixture.CreateClient();
        var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail(prefix));

        return (client, tokens.AccessToken);
    }
}
