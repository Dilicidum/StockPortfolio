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

public static class AlertsModule
{
    public const string ConnectionStringName = "Alerts";

    public const string SectionName = "Alerts";

    private const string PollingSectionName = "MarketData:Polling";

    public static IServiceCollection AddAlertsModule(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        var connectionString = config.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. Set "
                + $"ConnectionStrings:{ConnectionStringName} (or ConnectionStrings__{ConnectionStringName}). "
                + "Passing a null connection string to UseNpgsql throws later, from a stack that names "
                + "neither the key nor the file.");
        }

        services.AddDbContext<AlertsDbContext>(options => options.UseNpgsql(
            connectionString,
            npg =>
            {
                npg.MigrationsHistoryTable(
                    AlertsDbContext.MigrationsHistoryTableName,
                    AlertsDbContext.SchemaName);

                // Three attempts two seconds apart, not the six-attempt default: a stopped database must answer before the readiness probe times out. The cost is that EF now buffers every result set.
                npg.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(2), errorCodesToAdd: null);
            }));

        // Tagged, not bare: an untagged check joins no probe at all now that every MapHealthChecks filters on a tag.
        services.AddHealthChecks().AddDbContextCheck<AlertsDbContext>("postgres-alerts", tags: ["ready", "detail"]);

        services.AddScoped<IAlertSettingRepository, AlertSettingRepository>();
        services.AddScoped<IFiredAlertRepository, FiredAlertRepository>();

        services.AddSingleton<IAlertCooldownStore, RedisAlertCooldownStore>();

        services.AddScoped<AlertDispatcher>();
        services.AddScoped<IAlertEvaluator, AlertEvaluator>();
        services.AddScoped<IWatchedTickerReader, WatchedTickerReader>();

        services.AddSingleton(ReadOptions(config));

        services.AddAlertsHandlers();

        return services;
    }

    private static AlertsOptions ReadOptions(IConfiguration config)
    {
        var alerts = config.GetSection(SectionName);

        // The feed guards are measured in poll intervals, so they read the poller's own keys; a private copy would drift and silently suppress alerts.
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

    private static int Positive(string? configured, int fallback) =>
        int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        && parsed > 0
            ? parsed
            : fallback;
}
