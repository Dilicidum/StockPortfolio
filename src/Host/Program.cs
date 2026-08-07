using System.Text.Json;
using System.Text.Json.Serialization;
using StockPortfolio.Host.Adapters;
using StockPortfolio.Host.Extensions;
using StockPortfolio.Host.Middleware;
using StockPortfolio.Modules.Alerts.Api;
using StockPortfolio.Modules.Alerts.Infrastructure;
using StockPortfolio.Modules.Identity.Infrastructure;
using StockPortfolio.Modules.Identity.Api;
using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Infrastructure;
using StockPortfolio.Modules.MarketData.Api;
using StockPortfolio.Modules.Portfolio.Infrastructure;
using StockPortfolio.Modules.Portfolio.Api;
using StockPortfolio.Shared.Kernel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);

// Parsed once and handed to both connections; the backplane silently kept its own defaults when it took the raw string.
var redisOptions = RedisExtensions.ReadConnectionOptions(builder.Configuration);

// Before the modules: MarketData injects IConnectionMultiplexer and nothing in it says so.
builder.Services.AddStockPortfolioRedis(redisOptions);

builder.Services.AddStockPortfolioSignalR(redisOptions);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());

    options.SerializerOptions.Converters.Add(new MoneyJsonConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

    options.SerializerOptions.PropertyNameCaseInsensitive = false;
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

// Before the modules: the bearer scheme must be the default before any endpoint calls RequireAuthorization.
builder.Services.AddStockPortfolioAuthentication();
builder.Services.AddAuthorization();

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>();

if (corsOrigins is null || corsOrigins.Length == 0)
{
    throw new InvalidOperationException(
        "Configuration 'Cors:Origins' is missing or empty. The SPA is cross-origin in every deployment "
        + "target, so an empty origin list builds a policy that matches nothing and the API starts green "
        + "while every browser call fails. Set Cors__Origins__0 in the environment (compose and Bicep both "
        + "do), or Cors:Origins in appsettings.");
}

builder.Services.AddCors(options => options.AddPolicy("spa", policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddIdentityApi();

builder.Services.AddPortfolioModule(builder.Configuration);
builder.Services.AddPortfolioApi();

builder.Services.AddMarketDataModule(builder.Configuration);
builder.Services.AddMarketDataApi();

// After AddMarketDataModule: the protector depends on MarketData's key-ring store.
builder.Services.AddStockPortfolioDataProtection();

builder.Services.AddAlertsModule(builder.Configuration);
builder.Services.AddAlertsApi();

// Plain Add and after AddMarketDataModule: TryAdd here loses to MarketData's no-op observer, and no alert ever fires, silently.
builder.Services.AddScoped<IPollTargetSource, AlertsPollTargetSource>();
builder.Services.AddScoped<IPriceSampleObserver, AlertsPriceSampleObserver>();

builder.Services.ValidateAlertWindowFitsRetention(builder.Configuration);
builder.Services.ValidateSecretProtectorIsRegistered();

builder.Services.AddStockPortfolioHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors("spa");          // before authentication, so a 401 still carries CORS headers
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapIdentityEndpoints();
app.MapPortfolioEndpoints();
app.MapMarketDataEndpoints();
app.MapAlertsEndpoints();
app.MapStockPortfolioHealthChecks();

await app.RunAsync();

// NEVER add UseResponseCompression().

/// <summary>Exposed so WebApplicationFactory&lt;Program&gt; can boot the host in tests.</summary>
public partial class Program;
