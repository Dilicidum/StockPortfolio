using System.Net;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>What .Produces declares against what the route actually returned, now that no typed union does it.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class EndpointMetadataTests(ApiFixture fixture)
{
    /// <summary>The five names WithName gives the /api/auth routes.</summary>
    private static readonly string[] AuthRouteNames =
        ["Register", "Login", "Refresh", "Logout", "GetCurrentUser"];

    /// <summary>The five names WithName gives Portfolio's routes: four under /api/holdings, plus the dashboard.</summary>
    private static readonly string[] PortfolioRouteNames =
        ["GetHoldings", "AddHolding", "UpdateHolding", "RemoveHolding", "GetDashboard"];

    /// <summary>MarketData's two routes that ship in every environment; the dev nudge is not mapped in all.</summary>
    private static readonly string[] MarketDataRouteNames = ["GetMarketDataHealth", "SearchTickers"];

    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>The five routes, as theory data.</summary>
    public static TheoryData<string> AuthRoutes => [.. AuthRouteNames];

    /// <summary>The five Portfolio routes, as theory data.</summary>
    public static TheoryData<string> PortfolioRoutes => [.. PortfolioRouteNames];

    /// <summary>The MarketData routes, as theory data.</summary>
    public static TheoryData<string> MarketDataRoutes => [.. MarketDataRouteNames];

    /// <summary>Presses the button on the smoke detector: the two rules below filter, so the filter must match.</summary>
    [Fact]
    public void EndpointDataSource_ExposesTheFiveAuthRoutes() => ShouldExposeExactly(AuthRouteNames);

    /// <summary>The same button for the Portfolio half, which was added a phase later and could have been missed.</summary>
    [Fact]
    public void EndpointDataSource_ExposesTheFivePortfolioRoutes() => ShouldExposeExactly(PortfolioRouteNames);

    /// <summary>And for MarketData, whose routes ship in every environment — unlike the dev-only nudge.</summary>
    [Fact]
    public void EndpointDataSource_ExposesTheMarketDataRoutes() => ShouldExposeExactly(MarketDataRouteNames);

    /// <summary>The check the typed Results union used to make: a status the route emits must be a status it.</summary>
    [Theory]
    [InlineData("Register", "fresh", 201)]
    [InlineData("Register", "duplicate", 409)]
    [InlineData("Register", "short-password", 400)]
    [InlineData("Login", "good", 200)]
    [InlineData("Login", "wrong-password", 401)]
    [InlineData("Refresh", "valid", 200)]
    [InlineData("Refresh", "garbage", 401)]
    [InlineData("Logout", "bearer", 204)]
    [InlineData("Logout", "anonymous", 401)]
    [InlineData("GetCurrentUser", "bearer", 200)]
    [InlineData("GetCurrentUser", "anonymous", 401)]
    public async Task AuthRoute_DeclaresTheStatusItReturned(string routeName, string scenario, int expectedStatus)
    {
        await ShouldDeclareWhatItReturnedAsync(routeName, scenario, expectedStatus);
    }

    /// <summary>The Portfolio half of the same matrix. POST declares both 201 and 200, which is where drift hides.</summary>
    [Theory]
    [InlineData("GetHoldings", "bearer", 200)]
    [InlineData("GetHoldings", "anonymous", 401)]
    [InlineData("AddHolding", "fresh", 201)]
    [InlineData("AddHolding", "duplicate-ticker", 200)]
    [InlineData("AddHolding", "bad-ticker", 400)]
    [InlineData("UpdateHolding", "own", 200)]
    [InlineData("UpdateHolding", "stranger", 404)]
    [InlineData("RemoveHolding", "own", 204)]
    [InlineData("RemoveHolding", "missing", 404)]
    [InlineData("GetDashboard", "bearer", 200)]
    [InlineData("GetDashboard", "anonymous", 401)]
    public async Task PortfolioRoute_DeclaresTheStatusItReturned(
        string routeName,
        string scenario,
        int expectedStatus)
    {
        await ShouldDeclareWhatItReturnedAsync(routeName, scenario, expectedStatus);
    }

    /// <summary>The health route is anonymous by design, so 200 is the only status a caller can drive.
    /// Search is behind sign-in, and an unusable query is a 200 with an empty list rather than a 400.</summary>
    [Theory]
    [InlineData("GetMarketDataHealth", "anonymous", 200)]
    [InlineData("SearchTickers", "bearer", 200)]
    [InlineData("SearchTickers", "empty-query", 200)]
    [InlineData("SearchTickers", "anonymous", 401)]
    public async Task MarketDataRoute_DeclaresTheStatusItReturned(
        string routeName,
        string scenario,
        int expectedStatus)
    {
        await ShouldDeclareWhatItReturnedAsync(routeName, scenario, expectedStatus);
    }

    /// <summary>A declared failure that claims no problem+json is a lie about what the client will parse.</summary>
    [Theory]
    [MemberData(nameof(AuthRoutes))]
    public void AuthRoute_ProblemStatuses_DeclareProblemJson(string routeName) =>
        ShouldDeclareProblemJsonForEveryFailure(routeName);

    /// <summary>The same rule over the holdings routes, whose 401 and 500 come from the group rather than the route.</summary>
    [Theory]
    [MemberData(nameof(PortfolioRoutes))]
    public void PortfolioRoute_ProblemStatuses_DeclareProblemJson(string routeName) =>
        ShouldDeclareProblemJsonForEveryFailure(routeName);

    /// <summary>And over MarketData's, whose only declared failure is the 500 every route can reach.</summary>
    [Theory]
    [MemberData(nameof(MarketDataRoutes))]
    public void MarketDataRoute_ProblemStatuses_DeclareProblemJson(string routeName) =>
        ShouldDeclareProblemJsonForEveryFailure(routeName);

    private void ShouldDeclareProblemJsonForEveryFailure(string routeName)
    {
        var declared = DeclaredResponses(routeName);

        declared.ShouldContain(
            metadata => metadata.StatusCode >= 400,
            $"{routeName} declares no failure status at all, so this rule would pass by checking nothing.");

        var offenders = declared
            .Where(metadata => metadata.StatusCode >= 400)
            .Where(metadata => !metadata.ContentTypes.Contains(Wire.ProblemJson, StringComparer.Ordinal))
            .Select(metadata =>
                $"{routeName} declares {metadata.StatusCode} as ["
                + string.Join(", ", metadata.ContentTypes)
                + "]")
            .ToList();

        offenders.ShouldBeEmpty(
            "Every 4xx and 5xx these routes declare is served as RFC 7807, because AddProblemDetails "
                + "and UseStatusCodePages give even framework-generated statuses a body:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, offenders.Select(line => "  - " + line)));
    }

    private async Task ShouldDeclareWhatItReturnedAsync(string routeName, string scenario, int expectedStatus)
    {
        using var client = _fixture.CreateClient();

        var returned = (int)await CallAsync(client, routeName, scenario);

        returned.ShouldBe(
            expectedStatus,
            $"The '{scenario}' case of {routeName} no longer produces the status this theory was "
                + "written around, so the metadata assertion below would be checking the wrong one.");

        DeclaredResponses(routeName)
            .Select(metadata => metadata.StatusCode)
            .ShouldContain(
                returned,
                $"{routeName} returned {returned} but does not declare it. Endpoint handlers return "
                    + "Task<IResult>, so .Produces metadata is the only description of what a route "
                    + "emits — and it is what /openapi/v1.json, and therefore the SPA's contract, is "
                    + "generated from. Add the missing .Produces/.ProducesProblem to the route or its group.");
    }

    private void ShouldExposeExactly(string[] routeNames)
    {
        var found = EndpointsByName(routeNames).Keys.Order(StringComparer.Ordinal).ToList();

        found.ShouldBe(
            routeNames.Order(StringComparer.Ordinal),
            ignoreOrder: false,
            "The metadata rules below select endpoints by their WithName. If that selection finds "
                + "nothing — a renamed route, a route dropped from the group, a data source that is "
                + "not the one the host built — both rules pass over an empty set and enforce nothing.");
    }

    /// <summary>Reads the response metadata off the endpoint the host actually built.</summary>
    private IReadOnlyList<IProducesResponseTypeMetadata> DeclaredResponses(string routeName)
    {
        var endpoints = EndpointsByName([.. AuthRouteNames, .. PortfolioRouteNames, .. MarketDataRouteNames]);

        endpoints.ShouldContainKey(routeName);

        // Read from the built endpoint, not the source: a status declared on the group lands here too.
        return endpoints[routeName].Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
    }

    private Dictionary<string, Endpoint> EndpointsByName(string[] routeNames) =>
        _fixture.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Select(endpoint => (
                Name: endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                Endpoint: endpoint))
            .Where(pair => pair.Name is not null
                && routeNames.Contains(pair.Name, StringComparer.Ordinal))
            .ToDictionary(pair => pair.Name!, pair => pair.Endpoint, StringComparer.Ordinal);

    /// <summary>Drives one named scenario over real HTTP and reports the status it came back with.</summary>
    private static async Task<HttpStatusCode> CallAsync(HttpClient client, string routeName, string scenario)
    {
        switch (routeName, scenario)
        {
            case ("Register", "fresh"):
            {
                using var response = await Wire.RegisterAsync(
                    client,
                    Wire.UniqueEmail("metadata-register"),
                    Wire.ValidPassword);

                return response.StatusCode;
            }

            case ("Register", "duplicate"):
            {
                var email = Wire.UniqueEmail("metadata-duplicate");
                _ = await Wire.RegisterSucceedsAsync(client, email);

                using var response = await Wire.RegisterAsync(client, email, Wire.ValidPassword);

                return response.StatusCode;
            }

            case ("Register", "short-password"):
            {
                using var response = await Wire.RegisterAsync(
                    client,
                    Wire.UniqueEmail("metadata-short-password"),
                    "short");

                return response.StatusCode;
            }

            case ("Login", "good"):
            {
                var email = Wire.UniqueEmail("metadata-login");
                _ = await Wire.RegisterSucceedsAsync(client, email);

                using var response = await Wire.LoginAsync(client, email, Wire.ValidPassword);

                return response.StatusCode;
            }

            case ("Login", "wrong-password"):
            {
                var email = Wire.UniqueEmail("metadata-login-wrong");
                _ = await Wire.RegisterSucceedsAsync(client, email);

                using var response = await Wire.LoginAsync(client, email, "definitely-not-the-password");

                return response.StatusCode;
            }

            case ("Refresh", "valid"):
            {
                var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail("metadata-refresh"));

                using var response = await Wire.RefreshAsync(client, tokens.RefreshToken);

                return response.StatusCode;
            }

            case ("Refresh", "garbage"):
            {
                using var response = await Wire.RefreshAsync(client, "not-a-refresh-token-this-host-ever-issued");

                return response.StatusCode;
            }

            case ("Logout", "bearer"):
            {
                var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail("metadata-logout"));

                using var response = await Wire.LogoutAsync(client, tokens.AccessToken, tokens.RefreshToken);

                return response.StatusCode;
            }

            case ("Logout", "anonymous"):
            {
                using var response = await Wire.SendAsync(client, HttpMethod.Post, "/api/auth/logout");

                return response.StatusCode;
            }

            case ("GetCurrentUser", "bearer"):
            {
                var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail("metadata-me"));

                using var response = await Wire.SendAsync(
                    client,
                    HttpMethod.Get,
                    "/api/auth/me",
                    tokens.AccessToken);

                return response.StatusCode;
            }

            case ("GetCurrentUser", "anonymous"):
            {
                using var response = await Wire.SendAsync(client, HttpMethod.Get, "/api/auth/me");

                return response.StatusCode;
            }

            case ("GetHoldings", "bearer"):
            {
                var token = await SignedInAsync(client, "metadata-holdings");

                using var response = await Wire.SendAsync(client, HttpMethod.Get, "/api/holdings", token);

                return response.StatusCode;
            }

            case ("GetHoldings", "anonymous"):
            {
                using var response = await Wire.SendAsync(client, HttpMethod.Get, "/api/holdings");

                return response.StatusCode;
            }

            case ("AddHolding", "fresh"):
            {
                var token = await SignedInAsync(client, "metadata-add-fresh");

                using var response = await Wire.AddHoldingAsync(client, token, "AAPL", 10m, 100m);

                return response.StatusCode;
            }

            case ("AddHolding", "duplicate-ticker"):
            {
                var token = await SignedInAsync(client, "metadata-add-duplicate");

                using var first = await Wire.AddHoldingAsync(client, token, "AAPL", 10m, 100m);
                first.StatusCode.ShouldBe(HttpStatusCode.Created, await Wire.Describe(first));

                using var response = await Wire.AddHoldingAsync(client, token, "AAPL", 10m, 150m);

                return response.StatusCode;
            }

            case ("AddHolding", "bad-ticker"):
            {
                var token = await SignedInAsync(client, "metadata-add-bad");

                using var response = await Wire.AddHoldingAsync(client, token, "BRK.B", 10m, 100m);

                return response.StatusCode;
            }

            case ("UpdateHolding", "own"):
            {
                var token = await SignedInAsync(client, "metadata-update-own");
                var id = await OpenPositionAsync(client, token, "MSFT");

                using var response = await Wire.SendAsync(
                    client,
                    HttpMethod.Patch,
                    $"/api/holdings/{id}",
                    token,
                    new { quantity = 15m, price = 120m });

                return response.StatusCode;
            }

            case ("UpdateHolding", "stranger"):
            {
                var ownerToken = await SignedInAsync(client, "metadata-update-owner");
                var id = await OpenPositionAsync(client, ownerToken, "TSLA");

                var strangerToken = await SignedInAsync(client, "metadata-update-stranger");

                using var response = await Wire.SendAsync(
                    client,
                    HttpMethod.Patch,
                    $"/api/holdings/{id}",
                    strangerToken,
                    new { quantity = 1m, price = 1m });

                return response.StatusCode;
            }

            case ("RemoveHolding", "own"):
            {
                var token = await SignedInAsync(client, "metadata-remove-own");
                var id = await OpenPositionAsync(client, token, "NVDA");

                using var response = await Wire.SendAsync(
                    client,
                    HttpMethod.Delete,
                    $"/api/holdings/{id}",
                    token);

                return response.StatusCode;
            }

            case ("RemoveHolding", "missing"):
            {
                var token = await SignedInAsync(client, "metadata-remove-missing");

                using var response = await Wire.SendAsync(
                    client,
                    HttpMethod.Delete,
                    $"/api/holdings/{Guid.NewGuid()}",
                    token);

                return response.StatusCode;
            }

            case ("GetDashboard", "bearer"):
            {
                var token = await SignedInAsync(client, "metadata-dashboard");

                // A position first: an empty portfolio short-circuits before the read model materialises
                // anything, so the 200 would prove nothing about the projection behind it.
                _ = await OpenPositionAsync(client, token, "AAPL");

                using var response = await Wire.SendAsync(client, HttpMethod.Get, "/api/dashboard", token);

                return response.StatusCode;
            }

            case ("GetDashboard", "anonymous"):
            {
                using var response = await Wire.SendAsync(client, HttpMethod.Get, "/api/dashboard");

                return response.StatusCode;
            }

            case ("GetMarketDataHealth", "anonymous"):
            {
                using var response = await Wire.SendAsync(client, HttpMethod.Get, "/api/marketdata/health");

                return response.StatusCode;
            }

            case ("SearchTickers", "bearer"):
            {
                var token = await SignedInAsync(client, "metadata-search");

                using var response = await Wire.SearchTickersAsync(client, token, "appl");

                return response.StatusCode;
            }

            case ("SearchTickers", "empty-query"):
            {
                var token = await SignedInAsync(client, "metadata-search-empty");

                using var response = await Wire.SearchTickersAsync(client, token, string.Empty);

                return response.StatusCode;
            }

            case ("SearchTickers", "anonymous"):
            {
                using var response = await Wire.SearchTickersAsync(client, accessToken: null, "appl");

                return response.StatusCode;
            }

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(scenario),
                    scenario,
                    $"No scenario '{scenario}' is defined for the {routeName} route.");
        }
    }

    /// <summary>Registers a throwaway account and returns its access token.</summary>
    private static async Task<string> SignedInAsync(HttpClient client, string prefix) =>
        (await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail(prefix))).AccessToken;

    /// <summary>Opens one position and returns its id, so the update and delete scenarios have something to aim at.</summary>
    private static async Task<Guid> OpenPositionAsync(HttpClient client, string accessToken, string ticker)
    {
        using var response = await Wire.AddHoldingAsync(client, accessToken, ticker, 10m, 100m);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await Wire.Describe(response));

        return (await Wire.ListHoldingsAsync(client, accessToken)).ShouldHaveSingleItem().Id;
    }
}
