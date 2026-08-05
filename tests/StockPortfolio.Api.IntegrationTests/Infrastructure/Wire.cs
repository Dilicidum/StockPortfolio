using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

/// <summary>The token pair returned by register, login and refresh.</summary>
public sealed record AuthPayload(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt);

/// <summary>The body of GET /api/auth/me.</summary>
public sealed record UserPayload(Guid Id, string Email);

/// <summary>An amount as the API serialises it. Amount is a string on purpose — see MoneyJsonConverter.</summary>
public sealed record MoneyPayload(string Amount, string Currency);

/// <summary>The errors map of an RFC 9457 validation problem, keyed by field name.</summary>
public sealed record ValidationProblemPayload(Dictionary<string, string[]>? Errors);

/// <summary>One position as the API returns it.</summary>
public sealed record HoldingPayload(
    Guid Id,
    string Ticker,
    decimal Quantity,
    MoneyPayload AveragePrice,
    MoneyPayload Invested,
    bool IsVisible,
    DateTimeOffset UpdatedAt);

/// <summary>Thin helpers over the five /api/auth routes, so the tests read as assertions rather than as.</summary>
internal static class Wire
{
    /// <summary>Media type the API must use for RFC 7807 errors.</summary>
    public const string ProblemJson = "application/problem+json";

    /// <summary>A password comfortably over the 12-character floor.</summary>
    public const string ValidPassword = "correct-horse-battery-staple";

    /// <summary>Mints an address no other test can collide with.</summary>
    public static string UniqueEmail(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}@example.test";

    /// <summary>Posts to /api/auth/register.</summary>
    public static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/register", new { email, password });

    /// <summary>Posts to /api/auth/login.</summary>
    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    /// <summary>Posts to /api/auth/refresh.</summary>
    public static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });

    /// <summary>Posts to /api/auth/logout, which needs the access token as well as the refresh token.</summary>
    public static Task<HttpResponseMessage> LogoutAsync(
        HttpClient client,
        string accessToken,
        string refreshToken) =>
        SendAsync(client, HttpMethod.Post, "/api/auth/logout", accessToken, new { refreshToken });

    /// <summary>Registers a new account and returns its tokens, asserting the 201 on the way.</summary>
    public static async Task<AuthPayload> RegisterSucceedsAsync(
        HttpClient client,
        string email,
        string password = ValidPassword)
    {
        using var response = await RegisterAsync(client, email, password);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await Describe(response));

        return await ReadTokensAsync(response);
    }

    /// <summary>Reads a token pair out of a successful auth response.</summary>
    public static async Task<AuthPayload> ReadTokensAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var payload = await response.Content.ReadFromJsonAsync<AuthPayload>(JsonSerializerOptions.Web);

        payload.ShouldNotBeNull();
        payload.AccessToken.ShouldNotBeNullOrWhiteSpace();
        payload.RefreshToken.ShouldNotBeNullOrWhiteSpace();

        return payload;
    }

    /// <summary>Posts a purchase to /api/holdings.</summary>
    public static Task<HttpResponseMessage> AddHoldingAsync(
        HttpClient client,
        string accessToken,
        string ticker,
        decimal quantity,
        decimal price) =>
        SendAsync(client, HttpMethod.Post, "/api/holdings", accessToken, new { ticker, quantity, price });

    /// <summary>Reads /api/holdings, asserting the 200 on the way.</summary>
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

    /// <summary>Reads the field names a 400 blames, compared case-insensitively so casing is not the assertion.</summary>
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

    /// <summary>Signs an access token with the host's own key, carrying exactly the claims asked for and no others.</summary>
    public static string MintAccessToken(
        string signingKey,
        string issuer,
        string audience,
        DateTimeOffset expiresAt,
        IDictionary<string, object>? claims = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256),
            Claims = claims ?? new Dictionary<string, object>(StringComparer.Ordinal),
        });

    /// <summary>Sends a request carrying a bearer token.</summary>
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

    /// <summary>Renders a response for a failing assertion's message.</summary>
    public static async Task<string> Describe(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return $"{(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}";
    }
}
