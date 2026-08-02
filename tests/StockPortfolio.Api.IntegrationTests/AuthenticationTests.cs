using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>
/// The five <c>/api/auth</c> routes, driven end to end over HTTP against a real Postgres.
/// </summary>
/// <param name="fixture">The shared containers and host.</param>
[Collection(ApiCollectionDefinition.Name)]
public sealed class AuthenticationTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>Registering issues a usable session, and the same credentials sign in again.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task Register_ThenLogin_ReturnsTokens()
    {
        using var client = _fixture.CreateClient();
        var email = Wire.UniqueEmail("register-then-login");

        using var registered = await Wire.RegisterAsync(client, email, Wire.ValidPassword);
        registered.StatusCode.ShouldBe(HttpStatusCode.Created, await Wire.Describe(registered));

        var fromRegister = await Wire.ReadTokensAsync(registered);
        fromRegister.AccessExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);

        // 201 without a Location reads as an oversight. There is no GET /api/users/{id}, so the created
        // account is addressable only as the caller's own identity, and that is what the header says.
        registered.Headers.Location?.ToString().ShouldBe("/api/auth/me");

        using var loggedIn = await Wire.LoginAsync(client, email, Wire.ValidPassword);
        loggedIn.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(loggedIn));

        var fromLogin = await Wire.ReadTokensAsync(loggedIn);

        // A second sign-in is a second session, not a re-issue of the first.
        fromLogin.RefreshToken.ShouldNotBe(fromRegister.RefreshToken);
    }

    /// <summary>The second registration of one address conflicts rather than overwriting.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// The uniqueness guarantee is the database index, not a pre-<c>SELECT</c>: check-then-insert is a
    /// race two concurrent registrations both win. <c>UserRepository</c> catches SQLSTATE <c>23505</c>
    /// on the <c>ix_users_email</c> index specifically and turns it into <c>EmailAlreadyUsed</c>, so any
    /// <i>other</i> unique violation still surfaces as an exception instead of a misleading 409.
    /// </remarks>
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

    /// <summary>A password under the floor is a field-level 400, not a generic one.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// This asserts the whole shape-validation path: the <c>ValidationFilter&lt;RegisterUser&gt;</c>
    /// endpoint filter short-circuits before the handler, and <c>AddProblemDetails</c> renders RFC 7807.
    /// The <c>errors</c> key is <c>Password</c> with a capital P — FluentValidation names the property
    /// from the member expression, and it is not run through the JSON naming policy.
    /// </remarks>
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
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// Normalisation happens in <c>User.Create</c>, which is the only place it can be trusted: the
    /// lower-cased form is what the unique index keys on, so anything that normalises later would let
    /// <c>Foo@Bar.com</c> and <c>foo@bar.com</c> become two accounts.
    /// </remarks>
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

        // Registering the mixed-case form a second time conflicts, which is the property that matters:
        // two spellings, one account.
        using var again = await Wire.RegisterAsync(client, mixed, Wire.ValidPassword);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict, await Wire.Describe(again));
    }

    /// <summary>An anonymous call to a guarded route is rejected.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        using var client = _fixture.CreateClient();

        using var response = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }

    /// <summary>A bearer token resolves back to the account that owns it.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// <b>This test is the only automatic proof that <c>MapInboundClaims = false</c> is set.</b>
    /// <c>JwtBearerOptions.MapInboundClaims</c> defaults to <see langword="true"/> even though the
    /// underlying <c>JsonWebTokenHandler</c> defaults to <see langword="false"/> — the options object
    /// overrides the handler. Left at the default, <c>sub</c> arrives renamed to the long
    /// <c>http://schemas.xmlsoap.org/…/nameidentifier</c> URI, <c>IdentityEndpoints</c>' lookup for
    /// <c>"sub"</c> returns null, and every authenticated request 401s with nothing in the logs to
    /// explain it. If someone deletes that line, this assertion is what catches it.
    /// </remarks>
    [Fact]
    public async Task Me_WithValidToken_ReturnsEmail()
    {
        using var client = _fixture.CreateClient();
        var email = Wire.UniqueEmail("me");

        var tokens = await Wire.RegisterSucceedsAsync(client, email);

        using var response = await Wire.SendAsync(client, HttpMethod.Get, "/api/auth/me", tokens.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        var user = await response.Content.ReadFromJsonAsync<UserPayload>(
            JsonSerializerOptions.Web,
            TestContext.Current.CancellationToken);

        user.ShouldNotBeNull();
        user.Email.ShouldBe(email);
        user.Id.ShouldNotBe(Guid.Empty);
    }

    /// <summary>Signing out answers 204 and does not require a body.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// Both shapes are exercised because both are supported: the SPA signs out by dropping its local
    /// session and sends nothing, while a client that still holds the refresh token can hand it over so
    /// the long-lived half is retired immediately. Sign-out is idempotent, so an unknown or
    /// already-revoked token is still 204 — answering 404 would turn the endpoint into an oracle for
    /// which token strings exist.
    /// </remarks>
    [Fact]
    public async Task Logout_Returns204()
    {
        using var client = _fixture.CreateClient();

        var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail("logout"));

        using var withToken = await Wire.SendAsync(
            client,
            HttpMethod.Post,
            "/api/auth/logout",
            tokens.AccessToken,
            new { refreshToken = tokens.RefreshToken });

        withToken.StatusCode.ShouldBe(HttpStatusCode.NoContent, await Wire.Describe(withToken));

        // Idempotent: the same token again, and no body at all, are both still 204.
        using var repeated = await Wire.SendAsync(
            client,
            HttpMethod.Post,
            "/api/auth/logout",
            tokens.AccessToken,
            new { refreshToken = tokens.RefreshToken });

        repeated.StatusCode.ShouldBe(HttpStatusCode.NoContent, await Wire.Describe(repeated));

        using var withoutBody = await Wire.SendAsync(
            client,
            HttpMethod.Post,
            "/api/auth/logout",
            tokens.AccessToken);

        withoutBody.StatusCode.ShouldBe(HttpStatusCode.NoContent, await Wire.Describe(withoutBody));
    }

    /// <summary>Sign-out still needs a bearer token — it is not an anonymous route.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task Logout_WithoutToken_Returns401()
    {
        using var client = _fixture.CreateClient();

        using var response = await Wire.SendAsync(client, HttpMethod.Post, "/api/auth/logout");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }

    /// <summary>A wrong password and an unknown address give the identical answer.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// <c>LoginUserHandler</c> verifies against a fixed dummy hash when the account does not exist, so
    /// the two cases match in response <i>and</i> in timing. Telling them apart would turn login into an
    /// account enumerator.
    /// </remarks>
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

        // Compared field by field rather than as raw text: ProblemDetails carries a per-request
        // traceId, which differs by construction and says nothing about which account exists.
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
