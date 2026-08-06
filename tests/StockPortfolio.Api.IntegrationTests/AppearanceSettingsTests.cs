using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Npgsql;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

// The appearance settings pair under /api/settings, driven end to end over HTTP against a real Postgres.
[Collection(ApiCollectionDefinition.Name)]
public sealed class AppearanceSettingsTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    // A user who has never touched settings gets the default row, without anything being written.
    [Fact]
    public async Task Get_ForANewUser_ReturnsSystemAndEnglish()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "appearance-default");

        using var response = await Wire.SendAsync(client, HttpMethod.Get, Wire.AppearancePath, token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        var payload = await response.Content.ReadFromJsonAsync<AppearancePayload>(
            JsonSerializerOptions.Web, TestContext.Current.CancellationToken);

        payload.ShouldNotBeNull();
        payload.Theme.ShouldBe("system");
        payload.Language.ShouldBe("en");
    }

    // What was saved is what a following read returns.
    [Fact]
    public async Task Put_ThenGet_ReturnsWhatWasSaved()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "appearance-roundtrip");

        using var saved = await Wire.SendAsync(
            client,
            HttpMethod.Put,
            Wire.AppearancePath,
            token,
            new { theme = "dark", language = "uk" });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(saved));

        var savedPayload = await saved.Content.ReadFromJsonAsync<AppearancePayload>(
            JsonSerializerOptions.Web, TestContext.Current.CancellationToken);
        savedPayload.ShouldNotBeNull();
        savedPayload.Theme.ShouldBe("dark");
        savedPayload.Language.ShouldBe("uk");

        using var fetched = await Wire.SendAsync(client, HttpMethod.Get, Wire.AppearancePath, token);

        fetched.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(fetched));

        var fetchedPayload = await fetched.Content.ReadFromJsonAsync<AppearancePayload>(
            JsonSerializerOptions.Web, TestContext.Current.CancellationToken);
        fetchedPayload.ShouldNotBeNull();
        fetchedPayload.Theme.ShouldBe("dark");
        fetchedPayload.Language.ShouldBe("uk");
    }

    // Shape validation rejects a theme outside the allowed set before any handler runs.
    [Fact]
    public async Task Put_WithAnUnknownTheme_Returns400()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "appearance-bad-theme");

        using var response = await Wire.SendAsync(
            client,
            HttpMethod.Put,
            Wire.AppearancePath,
            token,
            new { theme = "purple", language = "en" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Wire.Describe(response));
        response.Content.Headers.ContentType!.MediaType.ShouldBe(Wire.ProblemJson);

        (await Wire.FailingFieldsAsync(response)).ShouldContain("theme");
    }

    // No token, no settings — the group's RequireAuthorization applies to both routes.
    [Fact]
    public async Task Get_Anonymous_Is401()
    {
        using var client = _fixture.CreateClient();

        using var response = await Wire.SendAsync(client, HttpMethod.Get, Wire.AppearancePath);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }

    // A media type the route cannot read is what actually produces a 415 — checked against the running host.
    [Fact]
    public async Task Put_WithWrongContentType_ReturnsWhateverItReallyReturns()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "appearance-415");

        using var request = new HttpRequestMessage(HttpMethod.Put, Wire.AppearancePath)
        {
            Content = new StringContent("""{"theme":"dark","language":"uk"}""", Encoding.UTF8, "text/plain"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType, await Wire.Describe(response));
    }

    // The default row is built in memory and never persisted, so no row exists after the first read.
    [Fact]
    public async Task Get_ForANewUser_WritesNothing()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "appearance-no-write");

        using var me = await Wire.SendAsync(client, HttpMethod.Get, "/api/auth/manage/info", token);
        me.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(me));

        var user = await me.Content.ReadFromJsonAsync<UserPayload>(
            JsonSerializerOptions.Web, TestContext.Current.CancellationToken);
        user.ShouldNotBeNull();

        // A string: user_preferences.user_id is a foreign key onto AspNetUsers.Id, which is text.
        var userId = await Wire.UserIdAsync(_fixture.Services, user.Email);

        using var response = await Wire.SendAsync(client, HttpMethod.Get, Wire.AppearancePath, token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        await using var connection = new NpgsqlConnection(_fixture.IdentityConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM identity.user_preferences WHERE user_id = @userId",
            connection);
        command.Parameters.AddWithValue("userId", userId);

        var count = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        count.ShouldBeOfType<long>().ShouldBe(0L);
    }

    private static async Task<string> SignedInAsync(HttpClient client, string prefix) =>
        (await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail(prefix))).AccessToken;
}
