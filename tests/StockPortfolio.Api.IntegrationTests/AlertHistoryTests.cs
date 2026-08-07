using System.Net;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

[Collection(ApiCollectionDefinition.Name)]
public sealed class AlertHistoryTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public async Task History_ForAUserWithNoAlerts_IsAnEmptyList()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "alert-history-empty");

        (await Wire.ListFiredAlertsAsync(client, token)).ShouldBeEmpty();
    }

    // The SPA calls /api/alerts with no trailing slash, and a group root is easy to map wrong.
    [Fact]
    public async Task History_IsServedAtTheGroupRoot_WithoutATrailingSlash()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "alert-history-root");

        using var response = await Wire.SendAsync(client, HttpMethod.Get, Wire.AlertHistoryPath, token);

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "MapGroup(\"/api/alerts\").MapGet(\"/\") is the whole of this route's path, and a 404 here "
                + "would be invisible to every test that remembered to add the slash: "
                + await Wire.Describe(response));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_000)]
    public async Task History_WithALimitOutsideTheRange_IsClampedRatherThanRejected(int limit)
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "alert-history-limit");

        (await Wire.ListFiredAlertsAsync(client, token, limit)).ShouldBeEmpty();
    }

    [Fact]
    public async Task History_IsScopedToTheCaller()
    {
        using var client = _fixture.CreateClient();

        var stranger = await SignedInAsync(client, "alert-history-stranger");

        (await Wire.ListFiredAlertsAsync(client, stranger)).ShouldBeEmpty();
    }

    private static async Task<string> SignedInAsync(HttpClient client, string prefix) =>
        (await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail(prefix))).AccessToken;
}
