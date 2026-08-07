using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Shouldly;

using StockPortfolio.Api.IntegrationTests.Infrastructure;
using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Api.IntegrationTests;

[Collection(ApiCollectionDefinition.Name)]
public sealed class DataProtectionPersistenceTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public void Store_ThenGetAll_ReturnsWhatWasStored()
    {
        var store = _fixture.Services.GetRequiredService<IKeyRingStore>();
        var friendlyName = $"key-{Guid.NewGuid():N}";
        var xml = $"<key id=\"{friendlyName}\" />";

        store.Store(friendlyName, xml);

        store.GetAll().ShouldContain(xml);
    }

    [Fact]
    public async Task Protect_ThenUnprotectOnASecondHost_ReturnsTheOriginalValue()
    {
        const string secret = "a-users-real-provider-key";
        string ciphertext;

        await using (var first = _fixture.CreateHostWithClock(TimeProvider.System))
        {
            ciphertext = first.Services.GetRequiredService<ISecretProtector>().Protect(secret);
        }

        await using var second = _fixture.CreateHostWithClock(TimeProvider.System);

        second.Services.GetRequiredService<ISecretProtector>().Unprotect(ciphertext).ShouldBe(secret);
    }

    // Two hosts agreeing proves nothing: SetApplicationName alone makes the default filesystem key ring shareable, so this reads the table and matches "key-{guid}", which only the real key manager writes.
    [Fact]
    public async Task Protect_WritesARowTheFrameworkItselfCreated()
    {
        await using var host = _fixture.CreateHostWithClock(TimeProvider.System);
        host.Services.GetRequiredService<ISecretProtector>().Protect($"probe-{Guid.NewGuid():N}");

        await using var connection = new NpgsqlConnection(_fixture.MarketDataConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM marketdata.data_protection_keys "
                + "WHERE friendly_name ~* '^key-[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'",
            connection);

        var count = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        count.ShouldBeOfType<long>().ShouldBeGreaterThan(0L);
    }
}
