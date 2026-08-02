using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>Token validation must reject a correctly signed token that carries no usable subject.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class AccessTokenSubjectTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>A signed, unexpired, correctly addressed token with no `sub` claim is not an authenticated caller.</summary>
    [Fact]
    public async Task GetCurrentUser_TokenWithNoSubjectClaim_Returns401()
    {
        using var client = _fixture.CreateClient();

        var token = MintWithClaims(claims: null);

        using var response = await Wire.SendAsync(client, HttpMethod.Get, "/api/auth/me", token);

        response.StatusCode.ShouldBe(
            HttpStatusCode.Unauthorized,
            "A token with no 'sub' names nobody. RequireAuthorization only asks for IsAuthenticated and "
            + "token validation does not require 'sub', so OnTokenValidated is the only thing that can "
            + "reject this. "
            + await Wire.Describe(response));
    }

    /// <summary>The test that pins OnTokenValidated: logout is authorized but never reads `sub`, so before the
    /// event existed a subject-less token got a cheerful 204 here.</summary>
    [Fact]
    public async Task Logout_TokenWithNoSubjectClaim_Returns401()
    {
        using var client = _fixture.CreateClient();

        var token = MintWithClaims(claims: null);

        using var response = await Wire.SendAsync(client, HttpMethod.Post, "/api/auth/logout", token);

        response.StatusCode.ShouldBe(
            HttpStatusCode.Unauthorized,
            "/api/auth/me returns 401 for a subject-less token through its own guard, so it cannot tell "
            + "whether authentication rejected the token. Logout has no such guard: a 204 here means "
            + "OnTokenValidated is not running and the /me assertions are passing for the wrong reason. "
            + await Wire.Describe(response));
    }

    /// <summary>The same mint with a `sub` that is not a Guid — signed by us, still nobody.</summary>
    [Fact]
    public async Task GetCurrentUser_TokenWithUnparseableSubjectClaim_Returns401()
    {
        using var client = _fixture.CreateClient();

        var token = MintWithClaims(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [JwtRegisteredClaimNames.Sub] = "not-a-guid",
        });

        using var response = await Wire.SendAsync(client, HttpMethod.Get, "/api/auth/me", token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }

    /// <summary>Proves the mint itself is good: the same helper with a real subject is accepted.</summary>
    [Fact]
    public async Task GetCurrentUser_MintedTokenWithARealSubject_IsAcceptedByTokenValidation()
    {
        using var client = _fixture.CreateClient();

        var registered = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail("minted-subject"));
        var subject = await SubjectOfAsync(client, registered.AccessToken);

        var token = MintWithClaims(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [JwtRegisteredClaimNames.Sub] = subject.ToString("D"),
        });

        using var response = await Wire.SendAsync(client, HttpMethod.Get, "/api/auth/me", token);

        // Without this the 401s above would also pass on a token the host rejects for some unrelated reason.
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));
    }

    private static async Task<Guid> SubjectOfAsync(HttpClient client, string accessToken)
    {
        using var response = await Wire.SendAsync(client, HttpMethod.Get, "/api/auth/me", accessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        var user = await response.Content.ReadFromJsonAsync<UserPayload>(JsonSerializerOptions.Web);

        user.ShouldNotBeNull();

        return user.Id;
    }

    private string MintWithClaims(IDictionary<string, object>? claims)
    {
        // Read back from the running host rather than restating it: a second copy of the key would drift.
        var configuration = _fixture.Services.GetRequiredService<IConfiguration>();

        return Wire.MintAccessToken(
            configuration["Jwt:SigningKey"]!,
            configuration["Jwt:Issuer"]!,
            configuration["Jwt:Audience"]!,
            DateTimeOffset.UtcNow.AddMinutes(10),
            claims);
    }
}
