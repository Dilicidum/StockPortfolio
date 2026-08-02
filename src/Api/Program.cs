using System.Text.Json;
using System.Text.Json.Serialization;
using StockPortfolio.Api.Extensions;
using StockPortfolio.Api.Middleware;
using StockPortfolio.Modules.Identity.Infrastructure;
using StockPortfolio.Modules.Identity.Api;

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────────────────────────────────────
// 1. Cross-cutting
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();                       // built-in; Swashbuckle is not used on .NET 9+
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // camelCase: the SPA reads accessToken / refreshToken / accessExpiresAt.
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

    // Strict is safe because nothing consumes this API yet. From Phase 2, money is serialised as a
    // STRING and needs a custom converter on the money type — which bypasses NumberHandling
    // entirely — rather than loosening this back to AllowReadingFromString.
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

// ─────────────────────────────────────────────────────────────────────────────
// 2. AuthN / AuthZ — sets MapInboundClaims = false; see AuthenticationExtensions
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddStockPortfolioAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

// ─────────────────────────────────────────────────────────────────────────────
// 3. CORS — exactly ONE layer. ACA's ingress.corsPolicy is deliberately unset in
//    infra/modules/containerapp-api.bicep: two layers on one response can emit a
//    duplicate Access-Control-Allow-Origin, which browsers reject outright.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options => options.AddPolicy("spa", policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// ─────────────────────────────────────────────────────────────────────────────
// 4. Modules. Infrastructure registers the DbContext, repositories and handlers
//    (and validates Jwt:SigningKey eagerly); the Api layer registers validators.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddIdentityApi();

// Must come AFTER the modules: a decorator only applies to descriptors that already exist.
builder.Services.DecorateHandlers();

builder.Services.AddStockPortfolioHealthChecks(builder.Configuration);

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
app.MapStockPortfolioHealthChecks();

await app.RunAsync().ConfigureAwait(false);

// ─────────────────────────────────────────────────────────────────────────────
// NEVER add UseResponseCompression(). It buffers text/event-stream and Phase 4's
// alert feed dies silently — no error, just no events ever arriving.
//
// NEVER call Migrate() here either. Two replicas racing the same migration
// corrupt the history table; migrations are the Migrator project's job.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can boot the host in tests.</summary>
public partial class Program;
