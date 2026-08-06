using System.Globalization;

namespace StockPortfolio.Api.Extensions;

/// <summary>Cross-module configuration checks the host runs at startup, before any request can arrive.</summary>
internal static class StartupInvariantExtensions
{
    private const string RetentionKey = "MarketData:Polling:RetentionMinutes";
    private const string MaxWindowKey = "Alerts:MaxWindowMinutes";
    private const int DefaultRetentionMinutes = 75;
    private const int DefaultMaxWindowMinutes = 60;

    /// <summary>Refuses to start when the price window is trimmed shorter than the longest alert a user may set.</summary>
    public static IServiceCollection ValidateAlertWindowFitsRetention(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var retention = Read(configuration, RetentionKey, DefaultRetentionMinutes);
        var maxWindow = Read(configuration, MaxWindowKey, DefaultMaxWindowMinutes);

        // Without this, somebody raises the window, nobody raises retention, and alerts stop firing
        // with no error anywhere - the evaluator simply never sees enough history to judge one.
        if (retention <= maxWindow)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{RetentionKey} is {retention} and {MaxWindowKey} is {maxWindow}. Price history must "
                        + $"outlast the longest window a user can configure, or their alerts silently stop "
                        + $"firing. Raise {RetentionKey} above {maxWindow}."));
        }

        return services;
    }

    private static int Read(IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[key], CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : fallback;
}
