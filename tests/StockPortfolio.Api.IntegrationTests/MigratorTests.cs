using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Api.IntegrationTests.Infrastructure;
using StockPortfolio.Migrator;

namespace StockPortfolio.Api.IntegrationTests;

[Collection(ApiCollectionDefinition.Name)]
public sealed class MigratorTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public void Migrator_RegistersADbContext_ForEveryModuleTheApiHostServes()
    {
        var services = new ServiceCollection();

        services.AddEveryMigratedModule(_fixture.MigratorConfiguration);

        var migrated = MigratedModules.DbContextTypesIn(services);

        // Pressed before the comparison: two empty lists are equal, and the rule would enforce nothing.
        migrated.ShouldNotBeEmpty(
            "AddEveryMigratedModule registered no DbContext at all, so the comparison below would be "
            + "comparing two empty lists and passing.");

        _fixture.HostDbContextTypes.ShouldNotBeEmpty(
            "The fixture captured no DbContext off the API host, so the comparison below would pass "
            + "however few modules the Migrator registers. ApiFixture reads them in ConfigureTestServices.");

        migrated.ShouldBe(
            _fixture.HostDbContextTypes,
            ignoreOrder: true,
            "The Migrator creates the schemas the API host then reads and writes. A module registered "
            + "in src/Host/Program.cs but missing from MigratedModules leaves the API starting cleanly, "
            + "serving every route, and failing each one on Npgsql 42P01 - which is docker compose up, "
            + "the P0 gate, broken.");
    }
}
