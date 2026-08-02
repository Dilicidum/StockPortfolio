using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

/// <summary>The token pair returned by register, login and refresh.</summary>
/// <param name="AccessToken">The signed JWT.</param>
/// <param name="RefreshToken">The opaque refresh token.</param>
/// <param name="AccessExpiresAt">When the access token stops being accepted.</param>
public sealed record AuthPayload(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt);

/// <summary>The body of <c>GET /api/auth/me</c>.</summary>
/// <param name="Id">The user's identifier.</param>
/// <param name="Email">The user's normalised address.</param>
public sealed record UserPayload(Guid Id, string Email);

/// <summary>
/// Thin helpers over the five <c>/api/auth</c> routes, so the tests read as assertions rather than
/// as plumbing.
/// </summary>
/// <remarks>
/// Deliberately no shared "arrange a user" fixture state: every test mints its own address from a
/// GUID. Tests in a collection run sequentially, but sequential is not ordered — a test that reused
/// another test's account would pass or fail depending on which one xUnit happened to schedule first.
/// </remarks>
internal static class Wire
{
    /// <summary>Media type the API must use for RFC 7807 errors.</summary>
    public const string ProblemJson = "application/problem+json";

    /// <summary>A password comfortably over the 12-character floor.</summary>
    public const string ValidPassword = "correct-horse-battery-staple";

    /// <summary>Mints an address no other test can collide with.</summary>
    /// <param name="prefix">A readable hint about which test owns it.</param>
    /// <returns>A unique, well-formed address.</returns>
    public static string UniqueEmail(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}@example.test";

    /// <summary>Posts to <c>/api/auth/register</c>.</summary>
    /// <param name="client">The client to use.</param>
    /// <param name="email">The address to register.</param>
    /// <param name="password">The password to register.</param>
    /// <returns>The raw response, so a test can assert on its status.</returns>
    public static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/register", new { email, password });

    /// <summary>Posts to <c>/api/auth/login</c>.</summary>
    /// <param name="client">The client to use.</param>
    /// <param name="email">The address to sign in with.</param>
    /// <param name="password">The password to sign in with.</param>
    /// <returns>The raw response.</returns>
    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    /// <summary>Posts to <c>/api/auth/refresh</c>.</summary>
    /// <param name="client">The client to use.</param>
    /// <param name="refreshToken">The token to exchange.</param>
    /// <returns>The raw response.</returns>
    public static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });

    /// <summary>Registers a new account and returns its tokens, asserting the 201 on the way.</summary>
    /// <param name="client">The client to use.</param>
    /// <param name="email">The address to register.</param>
    /// <param name="password">The password to register.</param>
    /// <returns>The issued token pair.</returns>
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
    /// <param name="response">The response to read.</param>
    /// <returns>The token pair, asserted non-empty.</returns>
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
    /// <param name="client">The client to use.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The request path.</param>
    /// <param name="accessToken">The access token, or <see langword="null"/> for an anonymous call.</param>
    /// <param name="body">An optional JSON body.</param>
    /// <returns>The raw response.</returns>
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
    /// <param name="response">The response to render.</param>
    /// <returns>Status line and body.</returns>
    public static async Task<string> Describe(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return $"{(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}";
    }
}
