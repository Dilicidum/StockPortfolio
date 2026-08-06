using System.Text.Json;
using System.Text.Json.Serialization;
using StockPortfolio.Api.Adapters;
using StockPortfolio.Api.Extensions;
using StockPortfolio.Api.Middleware;
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

// 1.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();                       // built-in; Swashbuckle is not used on .NET 9+
builder.Services.AddSingleton(TimeProvider.System);

// Before the modules: the missing-connection-string throw then fires before any module wiring, and
// MarketData injects IConnectionMultiplexer rather than depending on the health checks having registered it.
builder.Services.AddStockPortfolioRedis(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // camelCase: the SPA reads accessToken / refreshToken / accessExpiresAt.
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());

    // Money is decimal server-side and a string on the wire; a converter bypasses NumberHandling.Strict.
    options.SerializerOptions.Converters.Add(new MoneyJsonConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

    // Strict is safe because nothing consumes this API yet.
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

// 2. After AddIdentityModule below would be too late: nothing here needs the store, but the bearer
// scheme must be the default before any endpoint calls RequireAuthorization.
builder.Services.AddStockPortfolioAuthentication();
builder.Services.AddAuthorization();

// 3.
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

// 4.
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddIdentityApi();

builder.Services.AddPortfolioModule(builder.Configuration);
builder.Services.AddPortfolioApi();

builder.Services.AddMarketDataModule(builder.Configuration);
builder.Services.AddMarketDataApi();

// After AddMarketDataModule: the protector depends on MarketData's key-ring store. No eager warm-up:
// see CLAUDE.md, key ring vs migration job.
builder.Services.AddStockPortfolioDataProtection();

builder.Services.AddAlertsModule(builder.Configuration);
builder.Services.AddAlertsApi();

// The two halves of the poll cycle. MarketData states both needs in its own words and depends on
// nothing; these are the only place the two modules are named together.
//
// Both MUST be plain Add, and MUST come after AddMarketDataModule. That module registers a no-op
// observer with TryAdd, which skips only when the service type is ALREADY there - so it always wins
// the race and these two lines win by being last. Write TryAddScoped here and the no-op survives:
// the poller fetches prices, stores windows, and evaluates nothing, with no error anywhere.
builder.Services.AddScoped<IPollTargetSource, AlertsPollTargetSource>();
builder.Services.AddScoped<IPriceSampleObserver, AlertsPriceSampleObserver>();

// Retention belongs to MarketData and the window cap to Alerts, so this is the only place both are
// visible. A window longer than retention stops alerts firing and reports nothing.
builder.Services.ValidateAlertWindowFitsRetention(builder.Configuration);

// TryAdd cannot give ISecretProtector a default: a module's TryAdd always wins the race to be first,
// so a missing registration must fail loudly here rather than on the first key someone saves.
builder.Services.ValidateSecretProtectorIsRegistered();

// Must come AFTER the modules: a decorator only applies to descriptors that already exist.
// Not load-bearing for MarketData, which registers no ICommandHandler or IQueryHandler at all -
// the dashboard handler this phase adds is Portfolio's.
builder.Services.DecorateHandlers();

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
