using System.Text.Json;
using System.Text.Json.Serialization;
using StockPortfolio.Api.Extensions;
using StockPortfolio.Api.Middleware;
using StockPortfolio.Modules.Alerts.Infrastructure;
using StockPortfolio.Modules.Identity.Infrastructure;
using StockPortfolio.Modules.Identity.Api;
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

// 2.
builder.Services.AddStockPortfolioAuthentication(builder.Configuration);
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

builder.Services.AddAlertsModule(builder.Configuration);

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
app.MapStockPortfolioHealthChecks();

await app.RunAsync();

// NEVER add UseResponseCompression().

/// <summary>Exposed so WebApplicationFactory&lt;Program&gt; can boot the host in tests.</summary>
public partial class Program;
