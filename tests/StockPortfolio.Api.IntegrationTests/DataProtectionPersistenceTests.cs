using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Shouldly;

using StockPortfolio.Api.IntegrationTests.Infrastructure;
using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>Proves the encryption key ring lives in Postgres rather than the container filesystem.</summary>
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

    /// <summary>The whole reason the ring is in Postgres rather than the container filesystem: a second
    /// process, built against the same database after the first is gone, must still read what the first
    /// wrote.</summary>
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

    // Two hosts agreeing is not proof of Postgres on its own: SetApplicationName is exactly what makes the
    // framework's default filesystem key ring shareable between instances on one machine, so that test
    // would pass even with KeyRingXmlRepository never wired up. This one reads the table instead.
    //
    // The framework names its own elements "key-{guid}". Store_ThenGetAll writes "key-1" by hand, so the
    // prefix alone is not enough to tell them apart - the guid is. A row matching key- followed by a uuid
    // can only have come from the real key manager going through KeyRingXmlRepository, and if that
    // repository were not registered the framework would use local disk for the whole run and no such row
    // would exist anywhere.
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
