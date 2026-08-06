using System.Net;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>Search end to end, and the company names it leaves behind on the two tables that show them.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class TickerSearchTests(ApiFixture fixture)
{
    /// <summary>The name the keyless catalogue gives AAPL. The suite never runs against the live provider.</summary>
    private const string AppleName = "Apple Inc";

    private const string MicrosoftName = "Microsoft Corporation";

    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>The form behind it is signed in, so the route is too.</summary>
    [Fact]
    public async Task Search_Anonymous_Is401()
    {
        using var client = _fixture.CreateClient();

        using var response = await Wire.SearchTickersAsync(client, accessToken: null, "appl");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }

    /// <summary>`docker compose up` with no API key is the acceptance gate, and this is it working.</summary>
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

    /// <summary>Fuzzy on purpose: the exact-match rule belongs to the existence check, not to a list.</summary>
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

    /// <summary>Every suggestion must be a symbol the add-position form will then accept.</summary>
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

    /// <summary>q omitted entirely, which is what an unbound query string looks like on the wire.</summary>
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

    /// <summary>A search outage must not block someone recording a purchase they really made.</summary>
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

        // The form still works with search dead, which is the whole reason the empty list is a 200.
        using var added = await Wire.AddHoldingAsync(client, tokens.AccessToken, "AAPL", 1m, 10m);

        added.IsSuccessStatusCode.ShouldBeTrue(await Wire.Describe(added));
    }

    /// <summary>A bare array, matching GET /api/holdings rather than wrapping the list in an object.</summary>
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

    /// <summary>The cache learns from the search, and the holdings page reads it without touching a provider.</summary>
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

    /// <summary>Null, not absent: no client can tell an absent member from a null one.</summary>
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

    /// <summary>The holdings page must never wait on the provider, so a dead one must not cost it names.</summary>
    [Fact]
    public async Task Holdings_ProviderDown_StillCarryTheirCachedNames()
    {
        var (client, token) = await SignedInAsync("names-provider-down");

        // Warm on the shared host. Two hosts, one Redis container — which is the only reason the
        // dead-provider host below has a name to find at all.
        await WarmAsync(client, token, "aapl", "AAPL");

        await using var host = _fixture.CreateHostWithQuoteProvider(ScriptedQuoteProvider.ServingNothing);
        using var deadClient = host.CreateClient();

        // The same token: both hosts share the signing key, issuer and audience, and the JWT is self-contained.
        await AddSucceedsAsync(deadClient, token, "AAPL");

        var holdings = await Wire.ListHoldingsAsync(deadClient, token);

        Holding(holdings, "AAPL").Name.ShouldBe(
            AppleName,
            "the name came back empty with the provider down, so this page is reading it from the "
            + "provider rather than from the cache — which is the dependency it exists without");
    }

    /// <summary>Redis down means names disappear and nothing else changes. That is the correct thing to lose.</summary>
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

    /// <summary>Search results themselves are not cached, so a repeat query still asks the provider.</summary>
    [Fact]
    public async Task Search_RepeatedQuery_StillAnswers()
    {
        var (client, token) = await SignedInAsync("search-repeat");

        var first = await Wire.SearchSucceedsAsync(client, token, "appl");
        var second = await Wire.SearchSucceedsAsync(client, token, "appl");

        second.Select(match => match.Symbol).ShouldBe(first.Select(match => match.Symbol));
    }

    /// <summary>Runs a search purely for its side effect on the name cache.</summary>
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
