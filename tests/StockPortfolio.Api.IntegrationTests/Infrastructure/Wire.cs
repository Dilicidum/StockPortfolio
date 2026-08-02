using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

/// <summary>The token pair returned by register, login and refresh.</summary>
public sealed record AuthPayload(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt);

/// <summary>The body of GET /api/auth/me.</summary>
public sealed record UserPayload(Guid Id, string Email);

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
