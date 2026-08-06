using Microsoft.Extensions.DependencyInjection;

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
}
