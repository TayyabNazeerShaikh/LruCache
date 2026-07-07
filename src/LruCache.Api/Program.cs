using LruCache.Application;
using LruCache.Infrastructure;
using LruCache.Infrastructure.Caching;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddApplication();

// Bind "LruCache" section from appsettings.json → LruCacheOptions.Capacity
builder.Services.Configure<LruCacheOptions>(
    builder.Configuration.GetSection("LruCache"));

builder.Services.AddInfrastructure();

// Lightweight liveness probe — no external deps to check for this service.
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// Only redirect to HTTPS in development. Inside Docker the container runs
// HTTP-only on port 8080; TLS is terminated at the reverse-proxy boundary.
// Unconditional redirect would turn every health check into a 307 → dead HTTPS port.
if (app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseAuthorization();
app.MapControllers();

// Standard liveness endpoint — returns 200 when the process can serve requests.
app.MapHealthChecks("/health");

app.Run();

// Required by WebApplicationFactory<Program> in integration tests.
public partial class Program { }
