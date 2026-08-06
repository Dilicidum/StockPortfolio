using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>The appearance settings pair under /api/settings, driven end to end over HTTP against a real Postgres.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class AppearanceSettingsTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>A user who has never touched settings gets the default row, without anything being written.</summary>
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

    /// <summary>What was saved is what a following read returns.</summary>
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

    /// <summary>Shape validation rejects a theme outside the allowed set before any handler runs.</summary>
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

    /// <summary>No token, no settings — the group's RequireAuthorization applies to both routes.</summary>
    [Fact]
    public async Task Get_Anonymous_Is401()
    {
        using var client = _fixture.CreateClient();

        using var response = await Wire.SendAsync(client, HttpMethod.Get, Wire.AppearancePath);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }

    /// <summary>A media type the route cannot read is what actually produces a 415 — checked against the
    /// running host rather than assumed, and mirrored below in the endpoint's .Produces list.</summary>
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

    private static async Task<string> SignedInAsync(HttpClient client, string prefix) =>
        (await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail(prefix))).AccessToken;
}
