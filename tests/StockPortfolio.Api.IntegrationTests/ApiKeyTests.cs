using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using StockPortfolio.Api.IntegrationTests.Infrastructure;
using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Api.IntegrationTests;

[Collection(ApiCollectionDefinition.Name)]
public sealed class ApiKeyTests(ApiFixture fixture)
{
    private const string AGoodKey = "d1v3rs3-k3y-a1b2";

    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public async Task Post_WithAKeyTheProviderAccepts_Returns200WithLastFourOnly()
    {
        await using var host = _fixture.CreateHostWithQuoteProvider(ScriptedQuoteProvider.VerifyingKeyAs(KeyVerdict.Accepted));
        using var client = host.CreateClient();
        var token = await SignedInAsync(client, "apikey-accepted");

        using var response = await Wire.SaveApiKeyAsync(client, token, AGoodKey);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        var payload = await response.Content.ReadFromJsonAsync<ApiKeyStatusPayload>(
            JsonSerializerOptions.Web, TestContext.Current.CancellationToken);

        payload.ShouldNotBeNull();
        payload.Configured.ShouldBeTrue();
        payload.LastFour.ShouldBe("a1b2");
        payload.Rejected.ShouldBeFalse();
    }

    [Fact]
    public async Task Post_WithAKeyTheProviderRejects_Returns400AndStoresNothing()
    {
        await using var host = _fixture.CreateHostWithQuoteProvider(ScriptedQuoteProvider.VerifyingKeyAs(KeyVerdict.Rejected));
        using var client = host.CreateClient();
        var token = await SignedInAsync(client, "apikey-rejected");

        using var response = await Wire.SaveApiKeyAsync(client, token, "a-key-the-provider-refuses");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Wire.Describe(response));
        response.Content.Headers.ContentType!.MediaType.ShouldBe(Wire.ProblemJson);

        (await StatusAsync(client, token)).Configured.ShouldBeFalse();
    }

    [Fact]
    public async Task Post_WhenTheProviderCannotAnswer_Returns503AndStoresNothing()
    {
        await using var host = _fixture.CreateHostWithQuoteProvider(ScriptedQuoteProvider.VerifyingKeyAs(KeyVerdict.Unknown));
        using var client = host.CreateClient();
        var token = await SignedInAsync(client, "apikey-unknown");

        using var response = await Wire.SaveApiKeyAsync(client, token, "a-key-nobody-could-check");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, await Wire.Describe(response));
        response.Content.Headers.ContentType!.MediaType.ShouldBe(Wire.ProblemJson);

        (await StatusAsync(client, token)).Configured.ShouldBeFalse();
    }

    // The raw response text, not a deserialised field: a leak added later would land in a field a typed assertion never looks at.
    [Fact]
    public async Task Get_AfterSaving_NeverReturnsTheKeyAnywhereInTheBody()
    {
        await using var host = _fixture.CreateHostWithQuoteProvider(ScriptedQuoteProvider.VerifyingKeyAs(KeyVerdict.Accepted));
        using var client = host.CreateClient();
        var token = await SignedInAsync(client, "apikey-no-leak");

        using var saved = await Wire.SaveApiKeyAsync(client, token, AGoodKey);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(saved));

        var savedBody = await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        savedBody.ShouldNotContain(AGoodKey);

        using var fetched = await Wire.SendAsync(client, HttpMethod.Get, Wire.ApiKeySettingsPath, token);
        fetched.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(fetched));

        var fetchedBody = await fetched.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        fetchedBody.ShouldNotContain(AGoodKey);
    }

    [Fact]
    public async Task Delete_ThenGet_ReportsNotConfigured()
    {
        await using var host = _fixture.CreateHostWithQuoteProvider(ScriptedQuoteProvider.VerifyingKeyAs(KeyVerdict.Accepted));
        using var client = host.CreateClient();
        var token = await SignedInAsync(client, "apikey-delete");

        using var saved = await Wire.SaveApiKeyAsync(client, token, AGoodKey);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(saved));

        using var deleted = await Wire.SendAsync(client, HttpMethod.Delete, Wire.ApiKeySettingsPath, token);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await Wire.Describe(deleted));

        (await StatusAsync(client, token)).Configured.ShouldBeFalse();
    }

    [Fact]
    public async Task Post_WhenByokIsDisabled_Returns404()
    {
        await using var host = _fixture.CreateHostWithByokDisabled();
        using var client = host.CreateClient();
        var token = await SignedInAsync(client, "apikey-disabled");

        using var response = await Wire.SaveApiKeyAsync(client, token, AGoodKey);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, await Wire.Describe(response));
    }

    private static async Task<ApiKeyStatusPayload> StatusAsync(HttpClient client, string accessToken)
    {
        using var response = await Wire.SendAsync(client, HttpMethod.Get, Wire.ApiKeySettingsPath, accessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        var payload = await response.Content.ReadFromJsonAsync<ApiKeyStatusPayload>(
            JsonSerializerOptions.Web, TestContext.Current.CancellationToken);

        payload.ShouldNotBeNull();

        return payload;
    }

    private static async Task<string> SignedInAsync(HttpClient client, string prefix) =>
        (await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail(prefix))).AccessToken;
}
