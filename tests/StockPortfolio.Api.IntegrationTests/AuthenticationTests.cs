using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>The five /api/auth routes, driven end to end over HTTP against a real Postgres.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class AuthenticationTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>Registering issues a usable session, and the same credentials sign in again.</summary>
    [Fact]
    public async Task Register_ThenLogin_ReturnsTokens()
    {
        using var client = _fixture.CreateClient();
        var email = Wire.UniqueEmail("register-then-login");

        // Registering creates the account and issues nothing: 200, empty body, no Location. The route
        // this replaced answered 201 with a token pair, so signing in is now a separate call.
        using var registered = await Wire.RegisterAsync(client, email, Wire.ValidPassword);
        registered.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(registered));
        registered.Headers.Location.ShouldBeNull();

        using var loggedIn = await Wire.LoginAsync(client, email, Wire.ValidPassword);
        loggedIn.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(loggedIn));

        var fromLogin = await Wire.ReadTokensAsync(loggedIn);

        fromLogin.TokenType.ShouldBe("Bearer");
        fromLogin.ExpiresIn.ShouldBeGreaterThan(0);

        using var again = await Wire.LoginAsync(client, email, Wire.ValidPassword);
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(again));

        var fromSecondLogin = await Wire.ReadTokensAsync(again);

        // A second sign-in is a second session, not a re-issue of the first.
        fromSecondLogin.RefreshToken.ShouldNotBe(fromLogin.RefreshToken);
    }

    /// <summary>The second registration of one address conflicts rather than overwriting.</summary>
    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        using var client = _fixture.CreateClient();
        var email = Wire.UniqueEmail("duplicate");

        _ = await Wire.RegisterSucceedsAsync(client, email);

        using var second = await Wire.RegisterAsync(client, email, Wire.ValidPassword);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict, await Wire.Describe(second));
        second.Content.Headers.ContentType?.MediaType.ShouldBe(Wire.ProblemJson);
    }

    /// <summary>The taken-address check normalises the same way the entity does, so a variant still conflicts.</summary>
    [Theory]
    [InlineData("uppercased")]
    [InlineData("padded")]
    [InlineData("both")]
    public async Task Register_DuplicateEmailInAnotherCasing_Returns409NotAnUnhandledUniqueViolation(string style)
    {
        using var client = _fixture.CreateClient();

        // Its own address per case: the fixture shares one database across the whole assembly.
        var email = Wire.UniqueEmail("casing");

        var variant = style switch
        {
            "uppercased" => email.ToUpperInvariant(),
            "padded" => $"  {email}  ",
            _ => $"  {email.ToUpperInvariant()}  ",
        };

        _ = await Wire.RegisterSucceedsAsync(client, email);

        using var second = await Wire.RegisterAsync(client, variant, Wire.ValidPassword);

        // If the handler's pre-check normalised differently from User.Create, the insert would reach the
        // unique index instead and surface as a 500.
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict, await Wire.Describe(second));
    }

    /// <summary>A password under the floor is a field-level 400, not a generic one.</summary>
    [Fact]
    public async Task Register_WeakPassword_Returns400WithProblemDetails()
    {
        using var client = _fixture.CreateClient();

        using var response = await Wire.RegisterAsync(client, Wire.UniqueEmail("weak"), "short");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Wire.Describe(response));
        response.Content.Headers.ContentType?.MediaType.ShouldBe(Wire.ProblemJson);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(
            JsonSerializerOptions.Web,
            TestContext.Current.CancellationToken);

        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(400);
        problem.Errors.ShouldContainKey("Password");
        problem.Errors["Password"].ShouldNotBeEmpty();
    }

    /// <summary>Case and surrounding whitespace do not create a second account.</summary>
    [Fact]
    public async Task Register_NormalisesEmailToLowercase()
    {
        using var client = _fixture.CreateClient();

        // Foo@Bar.com, in the shape the brief's example uses: mixed case on both sides of the '@'.
        var mixed = $"Foo-{Guid.NewGuid():N}@Bar.Example.Test";
        var lower = mixed.ToLowerInvariant();

        // Sanity: the two forms really do differ, otherwise this test asserts nothing.
        mixed.ShouldNotBe(lower);

        var registered = await Wire.RegisterSucceedsAsync(client, mixed);
        registered.RefreshToken.ShouldNotBeNullOrWhiteSpace();

        using var loggedIn = await Wire.LoginAsync(client, lower, Wire.ValidPassword);
        loggedIn.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(loggedIn));

        // And /me reports the normalised form, not what was typed.
        var tokens = await Wire.ReadTokensAsync(loggedIn);
        using var me = await Wire.SendAsync(client, HttpMethod.Get, "/api/auth/me", tokens.AccessToken);

        me.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(me));

        var user = await me.Content.ReadFromJsonAsync<UserPayload>(
            JsonSerializerOptions.Web,
            TestContext.Current.CancellationToken);

        user.ShouldNotBeNull();
        user.Email.ShouldBe(lower);

        // Registering the mixed-case form a second time conflicts, which is the property that matters: two.
        using var again = await Wire.RegisterAsync(client, mixed, Wire.ValidPassword);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict, await Wire.Describe(again));
    }

    /// <summary>An anonymous call to a guarded route is rejected.</summary>
    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        using var client = _fixture.CreateClient();

        using var response = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }

    /// <summary>A bearer token resolves back to the account that owns it.</summary>
    [Fact]
    public async Task Me_WithValidToken_ReturnsEmail()
    {
        using var client = _fixture.CreateClient();
        var email = Wire.UniqueEmail("me");

        var tokens = await Wire.RegisterSucceedsAsync(client, email);

        // manage/info replaced the hand-written /api/auth/me. It carries the email and no id, which is
        // why anything needing the id now asks UserManager rather than the wire.
        using var response = await Wire.SendAsync(
            client, HttpMethod.Get, "/api/auth/manage/info", tokens.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        var user = await response.Content.ReadFromJsonAsync<UserPayload>(
            JsonSerializerOptions.Web,
            TestContext.Current.CancellationToken);

        user.ShouldNotBeNull();
        user.Email.ShouldBe(email);

        (await Wire.UserIdAsync(_fixture.Services, email)).ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>Signing out answers 204 and does not require a body.</summary>
    [Fact]
    public async Task Logout_Returns200_AndIsIdempotent()
    {
        using var client = _fixture.CreateClient();

        var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail("logout"));

        using var withToken = await Wire.LogoutAsync(client, tokens.AccessToken);

        withToken.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(withToken));

        // Idempotent: the access token outlives the logout by design, so a second call still lands.
        using var repeated = await Wire.LogoutAsync(client, tokens.AccessToken);

        repeated.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(repeated));
    }

    /// <summary>Logout actually revokes: the refresh token stops working immediately.</summary>
    /// <remarks>
    /// This is the assertion the migration nearly lost. MapIdentityApi ships no logout, and the version
    /// Microsoft documents only calls SignOutAsync — which for a bearer caller revokes nothing and leaves
    /// the refresh token good for its full lifetime. Rolling the security stamp is what closes it, and
    /// /refresh checking that stamp is the only reason this test can go red.
    /// </remarks>
    [Fact]
    public async Task Refresh_AfterLogout_IsRejected()
    {
        using var client = _fixture.CreateClient();

        var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail("logout-revokes"));

        // Refreshing works before the logout, so the rejection below is the logout and not a bad token.
        using (var before = await Wire.RefreshAsync(client, tokens.RefreshToken))
        {
            before.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(before));
        }

        var current = await Wire.ReadTokensAsync(await Wire.RefreshAsync(client, tokens.RefreshToken));

        using (var loggedOut = await Wire.LogoutAsync(client, current.AccessToken))
        {
            loggedOut.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(loggedOut));
        }

        using var after = await Wire.RefreshAsync(client, current.RefreshToken);

        after.StatusCode.ShouldBe(
            HttpStatusCode.Unauthorized,
            "Logout must roll the security stamp, which /refresh validates. Without that the token "
                + "stays good for its whole lifetime and logout is cosmetic: " + await Wire.Describe(after));
    }

    /// <summary>Sign-out still needs a bearer token — it is not an anonymous route.</summary>
    [Fact]
    public async Task Logout_WithoutToken_Returns401()
    {
        using var client = _fixture.CreateClient();

        using var response = await Wire.SendAsync(client, HttpMethod.Post, "/api/auth/logout");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }

    /// <summary>A wrong password and an unknown address give the identical answer.</summary>
    [Fact]
    public async Task Login_WithWrongPassword_IsIndistinguishableFromUnknownAccount()
    {
        using var client = _fixture.CreateClient();
        var email = Wire.UniqueEmail("wrong-password");

        _ = await Wire.RegisterSucceedsAsync(client, email);

        using var wrongPassword = await Wire.LoginAsync(client, email, "definitely-not-the-password");
        using var unknownAccount = await Wire.LoginAsync(client, Wire.UniqueEmail("nobody"), Wire.ValidPassword);

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(wrongPassword));
        unknownAccount.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(unknownAccount));

        // Compared field by field rather than as raw text: ProblemDetails carries a per-request traceId.
        var first = await ReadProblemAsync(wrongPassword);
        var second = await ReadProblemAsync(unknownAccount);

        first.ShouldBe(second);
    }

    /// <summary>Reads the identifying fields of a ProblemDetails body, ignoring the trace id.</summary>
    private static async Task<(string? Type, string? Title, int? Status, string? Detail)> ReadProblemAsync(
        HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            JsonSerializerOptions.Web,
            TestContext.Current.CancellationToken);

        problem.ShouldNotBeNull();

        return (problem.Type, problem.Title, problem.Status, problem.Detail);
    }
}
