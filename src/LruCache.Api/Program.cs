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

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Required by WebApplicationFactory<Program> in integration tests.
public partial class Program { }
