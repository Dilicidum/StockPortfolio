using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>
/// The evidence for brief P0 req 6 — «параметризація… конкатенація рядків у SQL неприпустима».
/// </summary>
/// <param name="fixture">The shared containers and host.</param>
/// <remarks>
/// <para>
/// <b>Why this test exists at all.</b> The repository uses EF Core LINQ and no raw SQL, which makes
/// parameterisation structural rather than a discipline anyone has to remember. But that is invisible:
/// a reviewer reading <c>context.Users.FirstOrDefaultAsync(u =&gt; u.Email == normalisedEmail)</c> is
/// being asked to take on trust that the provider emits <c>WHERE email = $1</c> rather than splicing
/// the string into the statement. This test replaces the trust with an observation.
/// </para>
/// <para>
/// <b>How it observes.</b> A <see cref="RecordingDbCommandInterceptor"/> is attached to the module's
/// <c>DbContext</c> for the whole run, capturing every <c>CommandText</c> and every
/// <c>DbParameter.Value</c> at the point EF hands the command to Npgsql — the last place the two are
/// still distinguishable. Hostile input is then driven through the ordinary HTTP endpoints, not
/// through a repository called directly, so the path under observation is the one a user reaches.
/// </para>
/// <para>
/// <b>What "hostile" means here.</b> The address carries a single quote, a comment introducer and a
/// tautology — <c>'-or-1=1--</c> — plus a random marker unique to this run. The marker is the whole
/// trick: it makes the assertion "this exact string never appears in any statement text" decidable
/// without having to reason about what a legitimate statement might coincidentally contain. It cannot
/// contain a space, because <c>User.Create</c> rejects addresses with spaces before any SQL is
/// reached, and a test whose input never gets to the database proves nothing.
/// </para>
/// <para>
/// <b>What would make this test lie, and the guards against it.</b> A recording interceptor that was
/// never attached would record nothing and the "marker never appears" assertion would pass vacuously.
/// So the test also asserts that statements <i>were</i> captured, that they include the
/// <c>identity.users</c> insert and select this request produced, that those statements carry
/// placeholders, and that the marker travelled as a <i>parameter value</i>. Positive and negative
/// assertions together: the value reached the database, and it reached it as data.
/// </para>
/// </remarks>
[Collection(ApiCollectionDefinition.Name)]
public sealed class ParameterisationTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>No user-supplied value ever reaches <c>CommandText</c>.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task Queries_NeverInlineUserInput_IntoCommandText()
    {
        using var client = _fixture.CreateClient();

        // Unique per run, so the assertion cannot be confused by anything another test left behind.
        var marker = $"sqli{Guid.NewGuid():N}";

        // A quote, a tautology and a comment introducer. No spaces: User.Create rejects those before
        // the value would ever reach the database, and an input that never arrives proves nothing.
        var hostileEmail = $"{marker}'-or-1=1--@example.test";

        // The password is hostile too, though it is hashed with Argon2id before it goes anywhere near
        // SQL, so it is the email that carries the weight of this test.
        var hostilePassword = $"{marker}');DROP-TABLE-identity.users;--";

        var before = _fixture.RecordedCommands.Commands.Count;

        using var registered = await Wire.RegisterAsync(client, hostileEmail, hostilePassword);
        registered.StatusCode.ShouldBe(HttpStatusCode.Created, await Wire.Describe(registered));

        // Exercise the read path as well as the write path: the SELECT behind login is where a naive
        // implementation would concatenate.
        using var loggedIn = await Wire.LoginAsync(client, hostileEmail, hostilePassword);
        loggedIn.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(loggedIn));

        var recorded = _fixture.RecordedCommands.Commands;

        // ── Guard: the interceptor is actually attached ──────────────────────────────────────────
        // Without this, everything below would pass against an empty list.
        recorded.Count.ShouldBeGreaterThan(
            before,
            "the recording interceptor captured no SQL for a request that demonstrably hit the database; "
            + "ModuleDbContextInterceptors is no longer wrapping the module's DbContextOptions");

        var thisRequest = recorded.Skip(before).ToArray();

        // ── Guard: we are looking at the right statements ────────────────────────────────────────
        thisRequest.ShouldContain(
            command => command.CommandText.Contains("identity.users", StringComparison.OrdinalIgnoreCase),
            "the registration and login should have produced statements against identity.users");

        // ── The claim: the hostile value is nowhere in any statement text ────────────────────────
        foreach (var command in recorded)
        {
            command.CommandText.ShouldNotContain(
                marker,
                Case.Insensitive,
                $"user input was concatenated into SQL: {command.CommandText}");
        }

        // ── The other half of the claim: it did travel, as data ──────────────────────────────────
        thisRequest.ShouldContain(
            command => command.ParameterValues.Any(
                value => value.Contains(marker, StringComparison.OrdinalIgnoreCase)),
            "the hostile email never reached the database at all, so nothing was proved about how it "
            + "would have been sent");

        // ── And the statements that carried it are parameterised ─────────────────────────────────
        var carriers = thisRequest
            .Where(command => command.ParameterValues.Any(
                value => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        carriers.ShouldNotBeEmpty();

        foreach (var command in carriers)
        {
            command.Parameters.ShouldNotBeEmpty();

            // Every parameter is referenced from the statement by name — EF's Npgsql provider writes
            // them as @p0, @p1, … — which is what makes the text a template rather than a rendering of
            // the data. (The wire protocol turns those into positional $1, $2 further down; either way
            // the value is bound, never parsed.)
            foreach (var parameter in command.Parameters)
            {
                command.CommandText.ShouldContain(
                    parameter.Name,
                    Case.Sensitive,
                    $"parameter '{parameter.Name}' is not referenced from the statement text, so the "
                    + $"value it carries may have been inlined instead: {command.CommandText}");
            }
        }
    }

    /// <summary>
    /// A value engineered to break out of a quoted literal is stored verbatim rather than executed.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// The behavioural companion to the assertion above. If any of this were concatenated, the
    /// registration would either fail with a syntax error or succeed while storing something other than
    /// what was sent; <c>/api/auth/me</c> reading the address back unchanged shows neither happened.
    /// The table also has to still exist afterwards, which the closing count proves.
    /// </remarks>
    [Fact]
    public async Task HostileInput_IsStoredVerbatim_AndExecutesNothing()
    {
        using var client = _fixture.CreateClient();

        var marker = $"sqli{Guid.NewGuid():N}";

        // Same constraint as above: no spaces, and nothing MailAddress rejects outright (a ';' in the
        // local part is not a legal address, so User.Create would 400 before any SQL ran and the test
        // would assert nothing). A quote, a tautology and a comment introducer are enough.
        var hostileEmail = $"{marker}'-or-1=1--@example.test";

        var tokens = await Wire.RegisterSucceedsAsync(client, hostileEmail);

        using var me = await Wire.SendAsync(client, HttpMethod.Get, "/api/auth/me", tokens.AccessToken);
        me.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(me));

        var user = await me.Content.ReadFromJsonAsync<UserPayload>(
            JsonSerializerOptions.Web,
            TestContext.Current.CancellationToken);

        // Round-tripped unchanged: nothing was escaped away, truncated, or interpreted. Compared after
        // deserialisation rather than against the raw body, because System.Text.Json's default encoder
        // renders the apostrophe as ' on the wire.
        user.ShouldNotBeNull();
        user.Email.ShouldBe(hostileEmail.ToLowerInvariant());

        // The table survived, which a successful injection would not have allowed.
        using var again = await Wire.LoginAsync(client, hostileEmail, Wire.ValidPassword);
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(again));
    }
}
