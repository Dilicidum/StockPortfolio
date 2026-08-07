using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;


namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

public sealed record AuthPayload(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);

public sealed record UserPayload(Guid Id, string Email);

public sealed record AppearancePayload(string Theme, string Language);

public sealed record DashboardSettingsPayload(int RefreshIntervalSeconds);

public sealed record ApiKeyStatusPayload(bool Configured, string? LastFour, bool Rejected);

public sealed record MoneyPayload(string Amount, string Currency);

public sealed record ValidationProblemPayload(Dictionary<string, string[]>? Errors);

public sealed record HoldingPayload(
    Guid Id,
    string Ticker,
    decimal Quantity,
    MoneyPayload AveragePrice,
    MoneyPayload Invested,
    bool IsVisible,
    DateTimeOffset UpdatedAt,
    string? Name);

public sealed record TickerMatchPayload(string Symbol, string Description);

public sealed record AlertSettingPayload(
    string Ticker,
    decimal ThresholdPercent,
    int WindowMinutes,
    bool Enabled);

public sealed record FiredAlertPayload(
    Guid Id,
    string Ticker,
    string Direction,
    string ChangePercent,
    string EndpointPercent,
    MoneyPayload TriggerPrice,
    MoneyPayload ReferencePrice,
    DateTimeOffset FiredAt,
    bool IsSimulated,
    string Reason);

public sealed record DashboardPositionPayload(
    Guid Id,
    string Ticker,
    decimal Quantity,
    MoneyPayload AveragePrice,
    MoneyPayload Cost,
    string? Name,
    MoneyPayload? CurrentPrice,
    MoneyPayload? MarketValue,
    MoneyPayload? Profit,
    string? ProfitPercent,
    string? Weight,
    DateTimeOffset? ObservedAt,
    bool IsLastKnown);

public sealed record DashboardTotalsPayload(
    MoneyPayload Value,
    MoneyPayload Cost,
    MoneyPayload Profit,
    string? ProfitPercent,
    int PositionCount,
    int PricedPositionCount);

public sealed record DashboardPayload(
    IReadOnlyList<DashboardPositionPayload> Positions,
    DashboardTotalsPayload Totals,
    DateTimeOffset AsOf,
    DateTimeOffset? StalestObservedAt);

internal static class Wire
{
    public const string DashboardPath = "/api/dashboard";

    public const string SearchPath = "/api/marketdata/search";

    public const string AlertSettingsPath = "/api/alerts/settings";

    public const string AppearancePath = "/api/settings/appearance";

    public const string DashboardSettingsPath = "/api/settings/dashboard";

    public const string ApiKeySettingsPath = "/api/settings/api-key";

    public const string AlertHistoryPath = "/api/alerts";

    public const string ProblemJson = "application/problem+json";

    // No digit, uppercase or symbol on purpose: the host turns those default rules off, and this is what proves it.
    public const string ValidPassword = "correct-horse-battery-staple";

    public static string UniqueEmail(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}@example.test";

    // marketdata:last:* is never trimmed and one Redis container outlives the assembly, so a reused symbol is already warm.
    public static string UniqueTicker()
    {
        // Five letters, the longest the shape allows, so it can never collide with the shorter literals the suite hardcodes.
        Span<char> symbol = stackalloc char[5];

        for (var index = 0; index < symbol.Length; index++)
        {
            symbol[index] = (char)('A' + Random.Shared.Next(26));
        }

        return new string(symbol);
    }

    public static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/register", new { email, password });

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    public static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });

    // Takes no refresh token: logout rolls the security stamp, retiring every refresh token this user holds at once.
    public static Task<HttpResponseMessage> LogoutAsync(HttpClient client, string accessToken) =>
        SendAsync(client, HttpMethod.Post, "/api/auth/logout", accessToken);

    // One call: this app maps its own register route, which signs the caller in; MapIdentityApi's answers 200 with an empty body.
    public static async Task<AuthPayload> RegisterSucceedsAsync(
        HttpClient client,
        string email,
        string password = ValidPassword)
    {
        using var response = await RegisterAsync(client, email, password);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await Describe(response));

        return await ReadTokensAsync(response);
    }

    public static async Task<AuthPayload> ReadTokensAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var payload = await response.Content.ReadFromJsonAsync<AuthPayload>(JsonSerializerOptions.Web);

        payload.ShouldNotBeNull();
        payload.AccessToken.ShouldNotBeNullOrWhiteSpace();
        payload.RefreshToken.ShouldNotBeNullOrWhiteSpace();

        return payload;
    }

    public static Task<HttpResponseMessage> AddHoldingAsync(
        HttpClient client,
        string accessToken,
        string ticker,
        decimal quantity,
        decimal price) =>
        SendAsync(client, HttpMethod.Post, "/api/holdings", accessToken, new { ticker, quantity, price });

    public static async Task<IReadOnlyList<HoldingPayload>> ListHoldingsAsync(
        HttpClient client,
        string accessToken)
    {
        using var response = await SendAsync(client, HttpMethod.Get, "/api/holdings", accessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Describe(response));

        var payload = await response.Content.ReadFromJsonAsync<List<HoldingPayload>>(JsonSerializerOptions.Web);

        payload.ShouldNotBeNull();

        return payload;
    }

    public static Task<HttpResponseMessage> SaveAlertSettingAsync(
        HttpClient client,
        string? accessToken,
        string ticker,
        decimal thresholdPercent,
        int windowMinutes,
        bool enabled = true) =>
        SendAsync(
            client,
            HttpMethod.Put,
            AlertSettingsPath,
            accessToken,
            new { ticker, thresholdPercent, windowMinutes, enabled });

    public static async Task<IReadOnlyList<AlertSettingPayload>> ListAlertSettingsAsync(
        HttpClient client,
        string accessToken)
    {
        using var response = await SendAsync(client, HttpMethod.Get, AlertSettingsPath, accessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Describe(response));

        var payload = await response.Content.ReadFromJsonAsync<List<AlertSettingPayload>>(
            JsonSerializerOptions.Web);

        payload.ShouldNotBeNull();

        return payload;
    }

    // The body is always sent, with a null ticker if none is named: a bodiless POST 415s against a required parameter.
    public static Task<HttpResponseMessage> SimulateAlertAsync(
        HttpClient client,
        string? accessToken,
        string? ticker = null) =>
        SendAsync(client, HttpMethod.Post, "/api/alerts/simulate", accessToken, new { ticker });

    public static async Task<IReadOnlyList<FiredAlertPayload>> ListFiredAlertsAsync(
        HttpClient client,
        string accessToken,
        int limit = 50)
    {
        using var response = await SendAsync(
            client,
            HttpMethod.Get,
            $"{AlertHistoryPath}?limit={limit.ToString(CultureInfo.InvariantCulture)}",
            accessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Describe(response));

        var payload = await response.Content.ReadFromJsonAsync<List<FiredAlertPayload>>(
            JsonSerializerOptions.Web);

        payload.ShouldNotBeNull();

        return payload;
    }

    public static Task<HttpResponseMessage> SaveApiKeyAsync(HttpClient client, string? accessToken, string apiKey) =>
        SendAsync(client, HttpMethod.Post, ApiKeySettingsPath, accessToken, new { apiKey });

    // The query is sent raw so an empty one can be exercised.
    public static Task<HttpResponseMessage> SearchTickersAsync(
        HttpClient client,
        string? accessToken,
        string query) =>
        SendAsync(client, HttpMethod.Get, $"{SearchPath}?q={Uri.EscapeDataString(query)}", accessToken);

    public static async Task<IReadOnlyList<TickerMatchPayload>> SearchSucceedsAsync(
        HttpClient client,
        string accessToken,
        string query)
    {
        using var response = await SearchTickersAsync(client, accessToken, query);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Describe(response));

        var payload = await response.Content.ReadFromJsonAsync<List<TickerMatchPayload>>(
            JsonSerializerOptions.Web);

        payload.ShouldNotBeNull();

        return payload;
    }

    public static async Task<string> ListHoldingsJsonAsync(HttpClient client, string accessToken)
    {
        using var response = await SendAsync(client, HttpMethod.Get, "/api/holdings", accessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Describe(response));

        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    public static async Task<DashboardPayload> GetDashboardAsync(HttpClient client, string accessToken)
    {
        using var response = await SendAsync(client, HttpMethod.Get, DashboardPath, accessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Describe(response));

        var payload = await response.Content.ReadFromJsonAsync<DashboardPayload>(JsonSerializerOptions.Web);

        payload.ShouldNotBeNull();

        return payload;
    }

    public static async Task<string> GetDashboardJsonAsync(HttpClient client, string accessToken)
    {
        using var response = await SendAsync(client, HttpMethod.Get, DashboardPath, accessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Describe(response));

        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    // Case-insensitive, so field-name casing is not what the assertion turns on.
    public static async Task<HashSet<string>> FailingFieldsAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemPayload>(
            JsonSerializerOptions.Web);

        problem.ShouldNotBeNull(await Describe(response));
        problem.Errors.ShouldNotBeNull(
            "The 400 carries no 'errors' member, so it is a plain problem document rather than the "
            + "validation problem this assertion reads: " + await Describe(response));

        return new HashSet<string>(problem.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? accessToken = null,
        object? body = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var request = new HttpRequestMessage(method, path);

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    public static async Task<string> Describe(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return $"{(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}";
    }
}
