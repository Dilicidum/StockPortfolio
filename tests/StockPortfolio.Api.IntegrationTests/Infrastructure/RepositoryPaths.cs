namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

internal static class RepositoryPaths
{
    private const string MarkerRelativePath = "db/init/01-roles.sql";

    public static string Root { get; } = FindRoot();

    public static string DatabaseInitDirectory { get; } = Path.Combine(Root, "db", "init");

    public static string RolesShellScript { get; } = Path.Combine(DatabaseInitDirectory, "00-roles.sh");

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
