using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using MkAIHub.Api.Api;
using MkAIHub.Api.Cli;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;

// CLI dispatch (python -m app.cli / alembic upgrade head equivalents).
if (args.Length > 0)
{
    var command = args[0];
    if (command is "create-admin" or "migrate" or "--help" or "-h" or "help")
    {
        return CliCommands.Dispatch(args);
    }
    if (!command.StartsWith("--"))
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        CliCommands.PrintUsage(Console.Error);
        return 2;
    }
    // Unknown leading options (e.g. --urls) fall through to the web host.
}

var builder = WebApplication.CreateBuilder(args);

// .env has lower precedence than real environment variables.
((IConfigurationBuilder)builder.Configuration).Sources.Insert(0, new DotEnvConfigurationSource
{
    Path = Path.Combine(Settings.FindRepositoryRoot(), ".env"),
});

// Early snapshot: validates configuration and drives builder-time wiring.
// The runtime Settings singleton below re-reads the final configuration so
// host-level overrides (tests, hosting layers) are honored.
var startupSettings = Settings.FromConfiguration(builder.Configuration);

// Match uvicorn: listen on 0.0.0.0:8000 unless URLs are configured explicitly.
var configuredUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
    ?? Environment.GetEnvironmentVariable("DOTNET_URLS");
if (string.IsNullOrEmpty(configuredUrls))
{
    builder.WebHost.UseUrls("http://0.0.0.0:8000");
}
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = null);

builder.Logging.ClearProviders();
builder.Logging.AddProvider(new JsonLoggerProvider(ApiLogging.ParseLevel(startupSettings.LogLevel)));

builder.Services.AddSingleton(_ => Settings.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton(serviceProvider =>
{
    var settings = serviceProvider.GetRequiredService<Settings>();
    return new CsrfSecret(
        settings.AppSecretKey is { Length: > 0 } secret
            ? System.Text.Encoding.UTF8.GetBytes(secret)
            : RandomNumberGenerator.GetBytes(32));
});
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var settings = serviceProvider.GetRequiredService<Settings>();
    var databasePath = Settings.ResolveDatabasePath(settings.DatabaseUrl);
    options.UseSqlite(Database.ConnectionStringFor(databasePath))
        .AddInterceptors(new Database.SqlitePragmaInterceptor());
});
builder.Services.AddScoped<Authenticator>();
builder.Services.AddControllers();

if (startupSettings.FrontendOrigins.Count > 0)
{
    builder.Services.AddCors(options => options.AddPolicy("frontend", policy => policy
        .WithOrigins(startupSettings.FrontendOrigins.ToArray())
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));
}

var app = builder.Build();

var settings = app.Services.GetRequiredService<Settings>();
var databasePath = Settings.ResolveDatabasePath(settings.DatabaseUrl);
Database.PrepareSqlite(databasePath);

app.UseMiddleware<ApiErrorLoggingMiddleware>();
app.UseRouting();
if (startupSettings.FrontendOrigins.Count > 0)
{
    app.UseCors("frontend");
}
app.UseEndpoints(endpoints => endpoints.MapControllers());

var spaTerminal = new SpaTerminalMiddleware(next: _ => Task.CompletedTask, settings);
var spaEnabled = spaTerminal.SpaEnabled;
if (spaEnabled)
{
    var fileProvider = new PhysicalFileProvider(Path.GetFullPath(settings.FrontendDist));
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
}
else
{
    app.MapGet("/", () => Results.Text(
        ApiJson.Serialize(new { service = settings.AppName, health = "/api/health" }),
        "application/json"));
}
app.Use((HttpContext context, Func<Task> next) => spaTerminal.InvokeAsync(context));

app.Run();
return 0;

/// <summary>Exposes the implicit Program class to WebApplicationFactory.</summary>
public partial class Program
{
}
