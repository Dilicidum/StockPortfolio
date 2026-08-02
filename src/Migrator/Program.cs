using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StockPortfolio.Modules.Identity.Infrastructure;

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
    // AddIdentityModule validates the Jwt section eagerly; the migrator never signs anything.
    ["Jwt:SigningKey"] = configuration["Jwt:SigningKey"]
                         ?? "migrator-placeholder-signing-key-unused-32b",
};

var migratorConfiguration = new ConfigurationBuilder()
    .AddConfiguration(configuration)
    .AddInMemoryCollection(overrides)
    .Build();

var services = new ServiceCollection();

// One line per module.
services.AddIdentityModule(migratorConfiguration);

var contextTypes = services
    .Where(descriptor => descriptor.ServiceType.IsSubclassOf(typeof(DbContext)))
    .Select(descriptor => descriptor.ServiceType)
    .Distinct()
    .OrderBy(type => type.Name, StringComparer.Ordinal)
    .ToList();

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

    var pending = (await context.Database.GetPendingMigrationsAsync().ConfigureAwait(false)).ToList();
    if (pending.Count == 0)
    {
        Console.WriteLine($"migrator: {contextType.Name} is up to date.");
        continue;
    }

    Console.WriteLine($"migrator: {contextType.Name} applying {pending.Count} migration(s): {string.Join(", ", pending)}");
    await context.Database.MigrateAsync().ConfigureAwait(false);
    Console.WriteLine($"migrator: {contextType.Name} done.");
}

Console.WriteLine($"migrator: complete, {contextTypes.Count} context(s) checked.");
return 0;
