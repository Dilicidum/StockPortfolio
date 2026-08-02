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

    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>The five routes, as theory data.</summary>
    public static TheoryData<string> AuthRoutes => [.. AuthRouteNames];

    /// <summary>Presses the button on the smoke detector: the two rules below filter, so the filter must match.</summary>
    [Fact]
    public void EndpointDataSource_ExposesTheFiveAuthRoutes()
    {
        var found = AuthEndpointsByName().Keys.Order(StringComparer.Ordinal).ToList();

        found.ShouldBe(
            AuthRouteNames.Order(StringComparer.Ordinal),
            ignoreOrder: false,
            "The metadata rules below select endpoints by their WithName. If that selection finds "
                + "nothing — a renamed route, a route dropped from the group, a data source that is "
                + "not the one the host built — both rules pass over an empty set and enforce nothing.");
    }

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

    /// <summary>A declared failure that claims no problem+json is a lie about what the client will parse.</summary>
    [Theory]
    [MemberData(nameof(AuthRoutes))]
    public void AuthRoute_ProblemStatuses_DeclareProblemJson(string routeName)
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

    /// <summary>Reads the response metadata off the endpoint the host actually built.</summary>
    private IReadOnlyList<IProducesResponseTypeMetadata> DeclaredResponses(string routeName)
    {
        var endpoints = AuthEndpointsByName();

        endpoints.ShouldContainKey(routeName);

        // Read from the built endpoint, not the source: a status declared on the group lands here too.
        return endpoints[routeName].Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
    }

    private Dictionary<string, Endpoint> AuthEndpointsByName() =>
        _fixture.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Select(endpoint => (
                Name: endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                Endpoint: endpoint))
            .Where(pair => pair.Name is not null
                && AuthRouteNames.Contains(pair.Name, StringComparer.Ordinal))
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

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(scenario),
                    scenario,
                    $"No scenario '{scenario}' is defined for the {routeName} route.");
        }
    }
}
