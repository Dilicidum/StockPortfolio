using System.Net;
using System.Net.Http.Headers;
using System.Text;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

[Collection(ApiCollectionDefinition.Name)]
public sealed class EndpointMetadataTests(ApiFixture fixture)
{
    private static readonly Dictionary<string, string[]> ExpectedRouteNames = new(StringComparer.Ordinal)
    {
        ["Identity"] = ["Register", "Login", "Refresh", "Logout", "GetCurrentUser", "GetAppearance", "SaveAppearance"],

        ["Portfolio"] =
        [
            "GetHoldings",
            "AddHolding",
            "UpdateHolding",
            "RemoveHolding",
            "SetHoldingVisibility",
            "GetDashboard",
            "GetDashboardSettings",
            "SaveDashboardSettings",
        ],

        ["MarketData"] = ["GetMarketDataHealth", "SearchTickers", "GetApiKeyStatus", "SaveApiKey", "RemoveApiKey"],

        // The alert feed is a SignalR hub, not a minimal-API endpoint, so it carries no route name or Produces metadata to check.
        ["Alerts"] =
        [
            "GetAlertSettings",
            "SaveAlertSetting",
            "GetAlerts",
            "SimulateAlert",
        ],
    };

    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    private static string[] AuthRouteNames => ExpectedRouteNames["Identity"];

    private static string[] PortfolioRouteNames => ExpectedRouteNames["Portfolio"];

    private static string[] MarketDataRouteNames => ExpectedRouteNames["MarketData"];

    private static string[] AlertsRouteNames => ExpectedRouteNames["Alerts"];

    public static TheoryData<string> AuthRoutes => [.. AuthRouteNames];

    public static TheoryData<string> PortfolioRoutes => [.. PortfolioRouteNames];

    public static TheoryData<string> MarketDataRoutes => [.. MarketDataRouteNames];

    public static TheoryData<string> AlertsRoutes => [.. AlertsRouteNames];

    [Fact]
    public void EndpointDataSource_ExposesTheIdentityRoutes() => ShouldExposeExactly(AuthRouteNames);

    [Fact]
    public void EndpointDataSource_ExposesTheEightPortfolioRoutes() => ShouldExposeExactly(PortfolioRouteNames);

    [Fact]
    public void EndpointDataSource_ExposesTheMarketDataRoutes() => ShouldExposeExactly(MarketDataRouteNames);

    [Fact]
    public void EndpointDataSource_ExposesTheAlertsRoutes() => ShouldExposeExactly(AlertsRouteNames);

    [Fact]
    public void EveryModuleWithAnApiAssembly_ContributesAtLeastOneMappedRoute()
    {
        var modules = MappedModules();

        // A rule that passes by finding nothing needs a companion that fails if the search finds nothing; raise this when a module's endpoints are mapped.
        modules.Count.ShouldBe(
            4,
            "The set of loaded .Api assemblies is derived, not listed, so an empty or short set would "
                + "make the loop below pass over nothing. Modules found: "
                + string.Join(", ", modules));

        var mapped = MappedRouteNames();

        foreach (var module in modules)
        {
            ExpectedRouteNames.ShouldContainKey(
                module,
                module + " ships an .Api assembly that no entry in ExpectedRouteNames names, so its "
                    + "routes are checked by nothing at all. Add its WithName names to the dictionary.");

            ExpectedRouteNames[module].Any(mapped.Contains).ShouldBeTrue(
                module + " ships an .Api assembly and contributes no route to the host's "
                    + "EndpointDataSource. The usual cause is a missing Map" + module + "Endpoints call "
                    + "in Program.cs — which compiles, registers, and serves nothing.");
        }
    }

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
    [InlineData("GetAppearance", "bearer", 200)]
    [InlineData("GetAppearance", "anonymous", 401)]
    [InlineData("SaveAppearance", "valid", 200)]
    [InlineData("SaveAppearance", "bad-theme", 400)]
    [InlineData("SaveAppearance", "wrong-content-type", 415)]
    [InlineData("SaveAppearance", "anonymous", 401)]
    public async Task AuthRoute_DeclaresTheStatusItReturned(string routeName, string scenario, int expectedStatus)
    {
        await ShouldDeclareWhatItReturnedAsync(routeName, scenario, expectedStatus);
    }

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
    [InlineData("GetDashboardSettings", "bearer", 200)]
    [InlineData("GetDashboardSettings", "anonymous", 401)]
    [InlineData("SaveDashboardSettings", "valid", 200)]
    [InlineData("SaveDashboardSettings", "out-of-range", 400)]
    [InlineData("SaveDashboardSettings", "wrong-content-type", 415)]
    [InlineData("SaveDashboardSettings", "anonymous", 401)]
    public async Task PortfolioRoute_DeclaresTheStatusItReturned(
        string routeName,
        string scenario,
        int expectedStatus)
    {
        await ShouldDeclareWhatItReturnedAsync(routeName, scenario, expectedStatus);
    }

    // The health route is anonymous, so 200 is the only status a caller can drive; an unusable search query is a 200 with an empty list.
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

    // Both 409s are driven, because they come from different checks.
    [Theory]
    [InlineData("GetAlertSettings", "bearer", 200)]
    [InlineData("GetAlertSettings", "anonymous", 401)]
    [InlineData("SaveAlertSetting", "held", 200)]
    [InlineData("SaveAlertSetting", "not-held", 409)]
    [InlineData("SaveAlertSetting", "window-over-cap", 409)]
    [InlineData("SaveAlertSetting", "bad-ticker", 400)]
    [InlineData("SaveAlertSetting", "wrong-content-type", 415)]
    [InlineData("SaveAlertSetting", "anonymous", 401)]
    [InlineData("GetAlerts", "bearer", 200)]
    [InlineData("GetAlerts", "silly-limit", 200)]
    [InlineData("GetAlerts", "anonymous", 401)]
    [InlineData("SimulateAlert", "watched", 202)]
    [InlineData("SimulateAlert", "nothing-to-simulate", 409)]
    [InlineData("SimulateAlert", "bad-ticker", 400)]
    [InlineData("SimulateAlert", "no-body", 400)]
    [InlineData("SimulateAlert", "wrong-content-type", 415)]
    [InlineData("SimulateAlert", "anonymous", 401)]
    public async Task AlertsRoute_DeclaresTheStatusItReturned(
        string routeName,
        string scenario,
        int expectedStatus)
    {
        await ShouldDeclareWhatItReturnedAsync(routeName, scenario, expectedStatus);
    }

    [Theory]
    [MemberData(nameof(AuthRoutes))]
    public void AuthRoute_ProblemStatuses_DeclareProblemJson(string routeName) =>
        ShouldDeclareProblemJsonForEveryFailure(routeName);

    [Theory]
    [MemberData(nameof(PortfolioRoutes))]
    public void PortfolioRoute_ProblemStatuses_DeclareProblemJson(string routeName) =>
        ShouldDeclareProblemJsonForEveryFailure(routeName);

    [Theory]
    [MemberData(nameof(MarketDataRoutes))]
    public void MarketDataRoute_ProblemStatuses_DeclareProblemJson(string routeName) =>
        ShouldDeclareProblemJsonForEveryFailure(routeName);

    [Theory]
    [MemberData(nameof(AlertsRoutes))]
    public void AlertsRoute_ProblemStatuses_DeclareProblemJson(string routeName) =>
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

    private static List<string> MappedModules() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetName().Name)
            .Where(name => name is not null
                && name.StartsWith("StockPortfolio.Modules.", StringComparison.Ordinal)
                && name.EndsWith(".Api", StringComparison.Ordinal))
            .Select(name => name!["StockPortfolio.Modules.".Length..^".Api".Length])
            .Order(StringComparer.Ordinal)
            .ToList();

    private HashSet<string> MappedRouteNames() =>
        _fixture.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

    private IReadOnlyList<IProducesResponseTypeMetadata> DeclaredResponses(string routeName)
    {
        var endpoints = EndpointsByName([.. ExpectedRouteNames.Values.SelectMany(names => names)]);

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

                using var response = await Wire.LogoutAsync(client, tokens.AccessToken);

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

            case ("GetAppearance", "bearer"):
            {
                var token = await SignedInAsync(client, "metadata-appearance-get");

                using var response = await Wire.SendAsync(client, HttpMethod.Get, Wire.AppearancePath, token);

                return response.StatusCode;
            }

            case ("GetAppearance", "anonymous"):
            {
                using var response = await Wire.SendAsync(client, HttpMethod.Get, Wire.AppearancePath);

                return response.StatusCode;
            }

            case ("SaveAppearance", "valid"):
            {
                var token = await SignedInAsync(client, "metadata-appearance-save");

                using var response = await Wire.SendAsync(
                    client,
                    HttpMethod.Put,
                    Wire.AppearancePath,
                    token,
                    new { theme = "dark", language = "uk" });

                return response.StatusCode;
            }

            case ("SaveAppearance", "bad-theme"):
            {
                var token = await SignedInAsync(client, "metadata-appearance-bad-theme");

                using var response = await Wire.SendAsync(
                    client,
                    HttpMethod.Put,
                    Wire.AppearancePath,
                    token,
                    new { theme = "purple", language = "en" });

                return response.StatusCode;
            }

            case ("SaveAppearance", "wrong-content-type"):
            {
                var token = await SignedInAsync(client, "metadata-appearance-415");

                using var request = new HttpRequestMessage(HttpMethod.Put, Wire.AppearancePath)
                {
                    Content = new StringContent(
                        """{"theme":"dark","language":"uk"}""",
                        Encoding.UTF8,
                        "text/plain"),
                };

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await client.SendAsync(request);

                return response.StatusCode;
            }

            case ("SaveAppearance", "anonymous"):
            {
                using var response = await Wire.SendAsync(
                    client,
                    HttpMethod.Put,
                    Wire.AppearancePath,
                    accessToken: null,
                    new { theme = "dark", language = "uk" });

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

                // A position first: an empty portfolio short-circuits before the read model materialises anything, so the 200 would prove nothing.
                _ = await OpenPositionAsync(client, token, "AAPL");

                using var response = await Wire.SendAsync(client, HttpMethod.Get, "/api/dashboard", token);

                return response.StatusCode;
            }

            case ("GetDashboard", "anonymous"):
            {
                using var response = await Wire.SendAsync(client, HttpMethod.Get, "/api/dashboard");

                return response.StatusCode;
            }

            case ("GetDashboardSettings", "bearer"):
            {
                var token = await SignedInAsync(client, "metadata-dashboard-settings-get");

                using var response = await Wire.SendAsync(
                    client,
                    HttpMethod.Get,
                    Wire.DashboardSettingsPath,
                    token);

                return response.StatusCode;
            }

            case ("GetDashboardSettings", "anonymous"):
            {
                using var response = await Wire.SendAsync(client, HttpMethod.Get, Wire.DashboardSettingsPath);

                return response.StatusCode;
            }

            case ("SaveDashboardSettings", "valid"):
            {
                var token = await SignedInAsync(client, "metadata-dashboard-settings-save");

                using var response = await Wire.SendAsync(
                    client,
                    HttpMethod.Put,
                    Wire.DashboardSettingsPath,
                    token,
                    new { refreshIntervalSeconds = 120 });

                return response.StatusCode;
            }

            case ("SaveDashboardSettings", "out-of-range"):
            {
                var token = await SignedInAsync(client, "metadata-dashboard-settings-bad-range");

                using var response = await Wire.SendAsync(
                    client,
                    HttpMethod.Put,
                    Wire.DashboardSettingsPath,
                    token,
                    new { refreshIntervalSeconds = 5 });

                return response.StatusCode;
            }

            case ("SaveDashboardSettings", "wrong-content-type"):
            {
                var token = await SignedInAsync(client, "metadata-dashboard-settings-415");

                using var request = new HttpRequestMessage(HttpMethod.Put, Wire.DashboardSettingsPath)
                {
                    Content = new StringContent(
                        """{"refreshIntervalSeconds":120}""",
                        Encoding.UTF8,
                        "text/plain"),
                };

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await client.SendAsync(request);

                return response.StatusCode;
            }

            case ("SaveDashboardSettings", "anonymous"):
            {
                using var response = await Wire.SendAsync(
                    client,
                    HttpMethod.Put,
                    Wire.DashboardSettingsPath,
                    accessToken: null,
                    new { refreshIntervalSeconds = 120 });

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

            case ("GetAlertSettings", "bearer"):
            {
                var token = await SignedInAsync(client, "metadata-alert-settings");

                using var response = await Wire.SendAsync(
                    client,
                    HttpMethod.Get,
                    Wire.AlertSettingsPath,
                    token);

                return response.StatusCode;
            }

            case ("GetAlertSettings", "anonymous"):
            {
                using var response = await Wire.SendAsync(client, HttpMethod.Get, Wire.AlertSettingsPath);

                return response.StatusCode;
            }

            case ("SaveAlertSetting", "held"):
            {
                var token = await SignedInAsync(client, "metadata-alert-save");
                _ = await OpenPositionAsync(client, token, "AAPL");

                using var response = await Wire.SaveAlertSettingAsync(client, token, "AAPL", 5m, 30);

                return response.StatusCode;
            }

            case ("SaveAlertSetting", "not-held"):
            {
                var token = await SignedInAsync(client, "metadata-alert-not-held");

                using var response = await Wire.SaveAlertSettingAsync(client, token, "MSFT", 5m, 30);

                return response.StatusCode;
            }

            case ("SaveAlertSetting", "window-over-cap"):
            {
                var token = await SignedInAsync(client, "metadata-alert-window");
                _ = await OpenPositionAsync(client, token, "AAPL");

                using var response = await Wire.SaveAlertSettingAsync(client, token, "AAPL", 5m, 61);

                return response.StatusCode;
            }

            case ("SaveAlertSetting", "bad-ticker"):
            {
                var token = await SignedInAsync(client, "metadata-alert-bad-ticker");

                using var response = await Wire.SaveAlertSettingAsync(client, token, "BRK.B", 5m, 30);

                return response.StatusCode;
            }

            case ("SaveAlertSetting", "wrong-content-type"):
            {
                var token = await SignedInAsync(client, "metadata-alert-415");

                // A media type the route cannot read is what produces 415; an absent body is a 400, which is why this sends text/plain.
                using var request = new HttpRequestMessage(HttpMethod.Put, Wire.AlertSettingsPath)
                {
                    Content = new StringContent(
                        """{"ticker":"AAPL","thresholdPercent":5,"windowMinutes":30,"enabled":true}""",
                        Encoding.UTF8,
                        "text/plain"),
                };

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await client.SendAsync(request);

                return response.StatusCode;
            }

            case ("SaveAlertSetting", "anonymous"):
            {
                using var response = await Wire.SaveAlertSettingAsync(
                    client,
                    accessToken: null,
                    "AAPL",
                    5m,
                    30);

                return response.StatusCode;
            }

            case ("GetAlerts", "bearer"):
            {
                var token = await SignedInAsync(client, "metadata-alert-history");

                using var response = await Wire.SendAsync(
                    client,
                    HttpMethod.Get,
                    Wire.AlertHistoryPath + "?limit=50",
                    token);

                return response.StatusCode;
            }

            case ("GetAlerts", "silly-limit"):
            {
                var token = await SignedInAsync(client, "metadata-alert-history-limit");

                // Clamped rather than refused, so this is a 200 and not the 400 a reader might expect.
                using var response = await Wire.SendAsync(
                    client,
                    HttpMethod.Get,
                    Wire.AlertHistoryPath + "?limit=100000",
                    token);

                return response.StatusCode;
            }

            case ("GetAlerts", "anonymous"):
            {
                using var response = await Wire.SendAsync(client, HttpMethod.Get, Wire.AlertHistoryPath);

                return response.StatusCode;
            }

            case ("SimulateAlert", "watched"):
            {
                var token = await SignedInAsync(client, "metadata-simulate");
                var ticker = Wire.UniqueTicker();

                using (var bought = await Wire.AddHoldingAsync(client, token, ticker, 10m, 100m))
                {
                    bought.StatusCode.ShouldBe(HttpStatusCode.Created, await Wire.Describe(bought));
                }

                using (var saved = await Wire.SaveAlertSettingAsync(client, token, ticker, 5m, 30))
                {
                    saved.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(saved));
                }

                using var response = await Wire.SimulateAlertAsync(client, token);

                return response.StatusCode;
            }

            case ("SimulateAlert", "nothing-to-simulate"):
            {
                var token = await SignedInAsync(client, "metadata-simulate-none");

                using var response = await Wire.SimulateAlertAsync(client, token);

                return response.StatusCode;
            }

            case ("SimulateAlert", "bad-ticker"):
            {
                var token = await SignedInAsync(client, "metadata-simulate-shape");

                using var response = await Wire.SimulateAlertAsync(client, token, "BRK.B");

                return response.StatusCode;
            }

            case ("SimulateAlert", "no-body"):
            {
                var token = await SignedInAsync(client, "metadata-simulate-bodiless");

                // An absent body is 400, not 415 — driven here because this route's client always sends a body precisely to avoid the other one.
                using var response = await Wire.SendAsync(
                    client,
                    HttpMethod.Post,
                    "/api/alerts/simulate",
                    token);

                return response.StatusCode;
            }

            case ("SimulateAlert", "wrong-content-type"):
            {
                var token = await SignedInAsync(client, "metadata-simulate-415");

                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/alerts/simulate")
                {
                    Content = new StringContent("""{"ticker":null}""", Encoding.UTF8, "text/plain"),
                };

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await client.SendAsync(request);

                return response.StatusCode;
            }

            case ("SimulateAlert", "anonymous"):
            {
                using var response = await Wire.SimulateAlertAsync(client, accessToken: null);

                return response.StatusCode;
            }

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(scenario),
                    scenario,
                    $"No scenario '{scenario}' is defined for the {routeName} route.");
        }
    }

    private static async Task<string> SignedInAsync(HttpClient client, string prefix) =>
        (await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail(prefix))).AccessToken;

    private static async Task<Guid> OpenPositionAsync(HttpClient client, string accessToken, string ticker)
    {
        using var response = await Wire.AddHoldingAsync(client, accessToken, ticker, 10m, 100m);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await Wire.Describe(response));

        return (await Wire.ListHoldingsAsync(client, accessToken)).ShouldHaveSingleItem().Id;
    }
}
