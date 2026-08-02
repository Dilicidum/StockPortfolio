using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StockPortfolio.Modules.Identity.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────────
//  StockPortfolio migrator
// ─────────────────────────────────────────────────────────────────────────────
//  Applies every module's EF migrations, connecting as the `migrator` role, which
//  owns the schemas and is the only role with CREATE.
//
//  This runs as its own container: `docker compose up` gates the API on
//  `migrations: service_completed_successfully`, and the ACA deployment runs the
//  same image as a Manual-trigger job. The API itself must NEVER call Migrate()
//  at startup - two replicas racing the same migration corrupt the history table.
//
//  HOW IT REACHES AN INTERNAL DbContext
//  Every <Module>DbContext is `internal` to its own Infrastructure assembly, so
//  this project cannot name one. It does not need to: it calls each module's
//  public registration seam, then walks the ServiceCollection for descriptors
//  whose service type derives from DbContext and migrates whatever it finds.
//  Adding a module later means adding one Add<Module>Module call below - nothing
//  else changes, and no type ever has to be made public to satisfy this tool.
// ─────────────────────────────────────────────────────────────────────────────

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

// Each module binds its own connection string by name. Point every one of them at the migrator
// credentials for the duration of this process - the service roles have DML only and cannot
// CREATE, so running migrations as them would fail with a permission error at the first DDL.
var overrides = new Dictionary<string, string?>(StringComparer.Ordinal)
{
    ["ConnectionStrings:Identity"] = migratorConnectionString,
    ["ConnectionStrings:Portfolio"] = migratorConnectionString,
    ["ConnectionStrings:MarketData"] = migratorConnectionString,
    ["ConnectionStrings:Alerts"] = migratorConnectionString,
    // AddIdentityModule validates the Jwt section eagerly, so it must be satisfiable here even
    // though the migrator never issues a token. Not a secret and never used to sign anything.
    ["Jwt:SigningKey"] = configuration["Jwt:SigningKey"]
                         ?? "migrator-placeholder-signing-key-unused-32b",
};

var migratorConfiguration = new ConfigurationBuilder()
    .AddConfiguration(configuration)
    .AddInMemoryCollection(overrides)
    .Build();

var services = new ServiceCollection();

// One line per module. Portfolio, MarketData and Alerts have no DbContext yet; they will be
// added here as their phases land.
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
