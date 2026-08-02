using System.Text.Json;
using System.Text.Json.Serialization;
using StockPortfolio.Api.Extensions;
using StockPortfolio.Api.Middleware;
using StockPortfolio.Modules.Identity.Infrastructure;
using StockPortfolio.Modules.Identity.Api;

var builder = WebApplication.CreateBuilder(args);

// 1.
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

    // Strict is safe because nothing consumes this API yet.
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

// 2.
builder.Services.AddStockPortfolioAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

// 3.
builder.Services.AddCors(options => options.AddPolicy("spa", policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// 4.
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

// NEVER add UseResponseCompression().

/// <summary>Exposed so WebApplicationFactory&lt;Program&gt; can boot the host in tests.</summary>
public partial class Program;
