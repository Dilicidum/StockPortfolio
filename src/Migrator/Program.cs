using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StockPortfolio.Migrator;

// Applies every module's EF migrations, connecting as the `migrator` role.

var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var migratorConnectionString = configuration.GetConnectionString("Migrator");
if (string.IsNullOrWhiteSpace(migratorConnectionString))
{
    Console.Error.WriteLine(
        "migrator: ConnectionStrings__Migrator is not set. It must be a connection string for the "
        + "`migrator` role, which owns the schemas. Refusing to run as anything else.");
    return 1;
}

// Each module binds its own connection string by name.
var overrides = new Dictionary<string, string?>(StringComparer.Ordinal)
{
    ["ConnectionStrings:Identity"] = migratorConnectionString,
    ["ConnectionStrings:Portfolio"] = migratorConnectionString,
    ["ConnectionStrings:MarketData"] = migratorConnectionString,
    ["ConnectionStrings:Alerts"] = migratorConnectionString,
};

var migratorConfiguration = new ConfigurationBuilder()
    .AddConfiguration(configuration)
    .AddInMemoryCollection(overrides)
    .Build();

var services = new ServiceCollection();

// The list lives in MigratedModules, not here: the integration fixture migrates through that same
// method, so dropping a module from it fails the test suite exactly as it fails docker compose up.
services.AddEveryMigratedModule(migratorConfiguration);

var contextTypes = MigratedModules.DbContextTypesIn(services);

if (contextTypes.Count == 0)
{
    Console.Error.WriteLine(
        "migrator: no DbContext registrations were found. A module seam probably stopped calling "
        + "AddDbContext, which would silently skip its migrations. Failing loudly instead.");
    return 1;
}

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();

foreach (var contextType in contextTypes)
{
    var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);

    var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
    if (pending.Count == 0)
    {
        Console.WriteLine($"migrator: {contextType.Name} is up to date.");
        continue;
    }

    Console.WriteLine($"migrator: {contextType.Name} applying {pending.Count} migration(s): {string.Join(", ", pending)}");
    await context.Database.MigrateAsync();
    Console.WriteLine($"migrator: {contextType.Name} done.");
}

Console.WriteLine($"migrator: complete, {contextTypes.Count} context(s) checked.");
return 0;
