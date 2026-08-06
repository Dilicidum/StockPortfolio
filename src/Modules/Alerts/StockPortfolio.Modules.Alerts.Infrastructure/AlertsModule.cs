using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Modules.Alerts.Application;
using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Application.Evaluation;
using StockPortfolio.Modules.Alerts.Application.Streaming;
using StockPortfolio.Modules.Alerts.Contracts;
using StockPortfolio.Modules.Alerts.Infrastructure.Persistence;
using StockPortfolio.Modules.Alerts.Infrastructure.Redis;

namespace StockPortfolio.Modules.Alerts.Infrastructure;

/// <summary>The Alerts module's entire public surface to the host.</summary>
public static class AlertsModule
{
    /// <summary>The ConnectionStrings key this module reads.</summary>
    public const string ConnectionStringName = "Alerts";

    /// <summary>The configuration section the module's tunable numbers are read from.</summary>
    public const string SectionName = "Alerts";

    /// <summary>The poller's section. Two guards are measured in poll intervals; see ReadOptions.</summary>
    private const string PollingSectionName = "MarketData:Polling";

    /// <summary>Registers the Alerts module: its DbContext, its two repositories and its handlers.</summary>
    public static IServiceCollection AddAlertsModule(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        // The only eager check: a threshold is a stored thing, so Alerts genuinely cannot run without a
        // database. MarketData's missing Finnhub key is the opposite case and must not throw.
        var connectionString = config.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. Set "
                + $"ConnectionStrings:{ConnectionStringName} (or ConnectionStrings__{ConnectionStringName}). "
                + "Passing a null connection string to UseNpgsql throws later, from a stack that names "
                + "neither the key nor the file.");
        }

        // AddDbContext, never AddDbContextFactory: the Migrator discovers contexts by scanning service
        // descriptors for a DbContext subclass, and only the former registers the context as its own type.
        // MigrationsHistoryTable is set here as well as design-time - HasDefaultSchema does not move it,
        // so without this line all four modules share one history table (efcore#24127).
        services.AddDbContext<AlertsDbContext>(options => options.UseNpgsql(
            connectionString,
            npg => npg.MigrationsHistoryTable(
                AlertsDbContext.MigrationsHistoryTableName,
                AlertsDbContext.SchemaName)));

        // This module's own readiness check. It lives here and not in the host because AlertsDbContext
        // is internal, and it borrows the scoped context rather than opening a connection, which is
        // what keeps four checks inside a Maximum Pool Size=2 budget.
        services.AddHealthChecks().AddDbContextCheck<AlertsDbContext>("postgres-alerts");

        services.AddScoped<IAlertSettingRepository, AlertSettingRepository>();
        services.AddScoped<IFiredAlertRepository, FiredAlertRepository>();

        // Redis, and therefore IConnectionMultiplexer, which AddStockPortfolioRedis registers on the
        // host and nothing in this file says. Delete that line from Program.cs and the first alert
        // fails rather than the startup.
        services.AddSingleton<IAlertCooldownStore, RedisAlertCooldownStore>();

        // No IAlertPublisher here. The fan-out is SignalR's, and its hub context is ASP.NET Core, which
        // this layer may not reference - so AddAlertsApi registers the publisher instead. Resolving an
        // AlertDispatcher without AddAlertsApi having run therefore fails at the first alert, not here.
        services.AddScoped<AlertDispatcher>();
        services.AddScoped<IAlertEvaluator, AlertEvaluator>();
        services.AddScoped<IWatchedTickerReader, WatchedTickerReader>();

        services.AddSingleton(ReadOptions(config));

        services.AddAlertsHandlers();

        return services;
    }

    /// <summary>Reads the tunable numbers. A missing or unusable value falls back rather than throwing.</summary>
    private static AlertsOptions ReadOptions(IConfiguration config)
    {
        // Deliberately not eager: a blank or absent Alerts:MaxWindowMinutes is a configuration file that
        // has not been told about this module yet, not a reason to refuse to start. The connection
        // string above is the only thing the module genuinely cannot run without.
        var alerts = config.GetSection(SectionName);

        // The two feed guards are measured in poll intervals, so they are read from the poller's own
        // keys rather than restated here. Reading a configuration value is not a module reference, and
        // a private copy of this number would drift the day somebody changed the polling interval -
        // silently, because a wrong gap guard suppresses alerts rather than failing.
        var polling = config.GetSection(PollingSectionName);

        var interval = TimeSpan.FromSeconds(
            Positive(polling["IntervalSeconds"], AlertsOptions.DefaultPollIntervalSeconds));

        var missed = Positive(polling["MaxMissedSamples"], AlertsOptions.DefaultMaxMissedSamples);

        return new AlertsOptions(
            Positive(alerts["MaxWindowMinutes"], AlertsOptions.DefaultMaxWindowMinutes),
            TimeSpan.FromMinutes(Positive(alerts["CooldownMinutes"], AlertsOptions.DefaultCooldownMinutes)),
            Positive(alerts["HistoryLimit"], AlertsOptions.DefaultHistoryLimit),
            Positive(polling["MinimumSamples"], AlertsOptions.DefaultMinimumSamples),
            interval * missed);
    }

    /// <summary>A blank, unparseable or non-positive setting is a file that has not been told, not a zero.</summary>
    private static int Positive(string? configured, int fallback) =>
        int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        && parsed > 0
            ? parsed
            : fallback;
}
