using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

// The evidence for brief P0 req 6: no user-supplied value may be concatenated into SQL.
[Collection(ApiCollectionDefinition.Name)]
public sealed class ParameterisationTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public async Task Queries_NeverInlineUserInput_IntoCommandText()
    {
        using var client = _fixture.CreateClient();

        // Unique per run, so the assertion cannot be confused by anything another test left behind.
        var marker = $"sqli{Guid.NewGuid():N}";

        var hostileEmail = $"{marker}'-or-1=1--@example.test";

        var hostilePassword = $"{marker}');DROP-TABLE-identity.AspNetUsers;--";

        var before = _fixture.RecordedCommands.Commands.Count;

        using var registered = await Wire.RegisterAsync(client, hostileEmail, hostilePassword);
        registered.StatusCode.ShouldBe(HttpStatusCode.Created, await Wire.Describe(registered));

        // The read path as well as the write path: the SELECT behind login is where a naive implementation would concatenate.
        using var loggedIn = await Wire.LoginAsync(client, hostileEmail, hostilePassword);
        loggedIn.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(loggedIn));

        var recorded = _fixture.RecordedCommands.Commands;

        recorded.Count.ShouldBeGreaterThan(
            before,
            "the recording interceptor captured no SQL for a request that demonstrably hit the database; "
            + "ModuleDbContextInterceptors is no longer wrapping the module's DbContextOptions");

        var thisRequest = recorded.Skip(before).ToArray();

        thisRequest.ShouldContain(
            command => command.CommandText.Contains("AspNetUsers", StringComparison.OrdinalIgnoreCase),
            "the registration and login should have produced statements against the user table. The "
            + "framework owns its name now, and Npgsql quotes it, so this matches the bare name rather "
            + "than a schema-qualified one");

        foreach (var command in recorded)
        {
            command.CommandText.ShouldNotContain(
                marker,
                Case.Insensitive,
                $"user input was concatenated into SQL: {command.CommandText}");
        }

        thisRequest.ShouldContain(
            command => command.ParameterValues.Any(
                value => value.Contains(marker, StringComparison.OrdinalIgnoreCase)),
            "the hostile email never reached the database at all, so nothing was proved about how it "
            + "would have been sent");

        var carriers = thisRequest
            .Where(command => command.ParameterValues.Any(
                value => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        carriers.ShouldNotBeEmpty();

        foreach (var command in carriers)
        {
            command.Parameters.ShouldNotBeEmpty();

            // Every parameter is referenced from the statement by name — EF's Npgsql provider writes them as @p0.
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

    [Fact]
    public async Task HostileInput_IsStoredVerbatim_AndExecutesNothing()
    {
        using var client = _fixture.CreateClient();

        var marker = $"sqli{Guid.NewGuid():N}";

        // No spaces, and nothing MailAddress rejects outright.
        var hostileEmail = $"{marker}'-or-1=1--@example.test";

        var tokens = await Wire.RegisterSucceedsAsync(client, hostileEmail);

        using var me = await Wire.SendAsync(client, HttpMethod.Get, "/api/auth/me", tokens.AccessToken);
        me.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(me));

        var user = await me.Content.ReadFromJsonAsync<UserPayload>(
            JsonSerializerOptions.Web,
            TestContext.Current.CancellationToken);

        // Round-tripped unchanged, compared against what was typed: Identity normalises into NormalizedEmail and leaves Email as entered.
        user.ShouldNotBeNull();
        user.Email.ShouldBe(hostileEmail);

        using var again = await Wire.LoginAsync(client, hostileEmail, Wire.ValidPassword);
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(again));
    }
}
