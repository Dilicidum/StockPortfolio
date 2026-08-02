namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

/// <summary>Locates the repository root from the test binary's location.</summary>
internal static class RepositoryPaths
{
    /// <summary>The file that identifies the repository root.</summary>
    private const string MarkerRelativePath = "db/init/01-roles.sql";

    /// <summary>Gets the absolute path of the repository root.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>Gets the absolute path of db/init, mounted into the container at /db/init.</summary>
    public static string DatabaseInitDirectory { get; } = Path.Combine(Root, "db", "init");

    /// <summary>Gets the absolute path of the wrapper the Postgres entrypoint executes.</summary>
    public static string RolesShellScript { get; } = Path.Combine(DatabaseInitDirectory, "00-roles.sh");

    /// <summary>Gets the absolute path of the single source of truth for roles, schemas and grants.</summary>
    public static string RolesSqlScript { get; } = Path.Combine(DatabaseInitDirectory, "01-roles.sql");

    private static string FindRoot()
    {
        var marker = MarkerRelativePath.Split('/');

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. marker]);

            if (File.Exists(candidate))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not find the repository root: no ancestor of '{AppContext.BaseDirectory}' contains "
            + $"'{MarkerRelativePath}'. The integration tests mount the real db/init scripts into the "
            + "Postgres container, so they cannot run from a detached copy of the test binaries.");
    }
}
