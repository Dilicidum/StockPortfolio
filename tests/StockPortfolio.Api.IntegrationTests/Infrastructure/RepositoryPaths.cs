namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Locates the repository root from the test binary's location.
/// </summary>
/// <remarks>
/// The fixture mounts the <i>real</i> <c>db/init</c> scripts into the Postgres container rather than
/// a copy embedded in this project, so the isolation these tests assert is the isolation that
/// actually ships. That means the tests need a path to the repository, and
/// <c>Directory.Build.props</c> sets <c>ArtifactsPath</c>, so the binary lives at
/// <c>artifacts/bin/&lt;project&gt;/&lt;config&gt;</c> — a depth that is a build-configuration detail,
/// not something to hardcode. Walking up until the marker file appears survives any layout change.
/// </remarks>
internal static class RepositoryPaths
{
    /// <summary>The file that identifies the repository root. Also the file the fixture mounts.</summary>
    private const string MarkerRelativePath = "db/init/01-roles.sql";

    /// <summary>Gets the absolute path of the repository root.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>Gets the absolute path of <c>db/init</c>, mounted into the container at <c>/db/init</c>.</summary>
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
