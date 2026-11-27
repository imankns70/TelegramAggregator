using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Telegram.Bot;
using TelegramAggregator.WebAPI.BackgroundServices;
using TelegramAggregator.WebAPI.Data;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Models.FootbalSettings;
using TelegramAggregator.WebAPI.Models.LiveBoard;
using TelegramAggregator.WebAPI.Services;
using TelegramAggregator.WebAPI.Services.Dashboard;
using TelegramAggregator.WebAPI.Services.LiveBoard;

if (args.Contains("--install-browsers", StringComparer.Ordinal))
{
    Environment.ExitCode = Microsoft.Playwright.Program.Main(new[] { "install", "--with-deps", "chromium" });
    return;
}

var configurationArgs = args.Where(argument => argument != "--migrate-only").ToArray();
var builder = WebApplication.CreateBuilder(configurationArgs);
// Preserve deployment configuration precedence above the scoring file.
builder.Configuration
    .AddJsonFile("footbalScoring.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(configurationArgs);

if (string.IsNullOrWhiteSpace(builder.Configuration[WebHostDefaults.ServerUrlsKey])
    && ushort.TryParse(builder.Configuration["PORT"], out var platformPort)
    && platformPort > 0)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{platformPort}");
}

builder.AddSeriLogLogger();
builder.Services.Configure<AppSettings>(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Set ConnectionStrings__DefaultConnection to the Npgsql connection string.");
}

builder.Services.AddDbContext<TelegramAggregatorContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddRepositories();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSingleton<IPlaywrightService, PlaywrightService>();
builder.Services.AddHttpClient<IHttpHelperService, HttpHelperService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
    {
        NoCache = true
    };
});
builder.Services.AddScoped<IFootballForiDashboardService, FootballForiDashboardService>();
builder.Services.AddScoped<IRssParserService, RssParserService>();
builder.Services.AddScoped<IImageExtractorService, ImageExtractorService>();
builder.Services.AddScoped<IFullDecriptionExtractorService, FullDecriptionExtractorService>();
builder.Services.AddScoped<IFootball360ScraperService, Football360ScraperService>();
builder.Services.AddScoped<FootballforiService>();
builder.Services.AddSingleton<ITelegramBotClient>(services =>
{
    var token = services.GetRequiredService<IOptions<AppSettings>>().Value.FootbalForiSettings.BotToken;
    if (string.IsNullOrWhiteSpace(token))
    {
        throw new InvalidOperationException("Set FootbalForiSettings__BotToken before enabling publishing.");
    }

    return new TelegramBotClient(token);
});

if (builder.Configuration.GetValue<bool>("Worker:PublishingEnabled"))
{
    var channelId = builder.Configuration.GetValue<long>("FootbalForiSettings:ChannelId");
    if (!FootbalForiSettings.IsPublishingChannelAllowed(channelId))
    {
        throw new InvalidOperationException("Telegram publishing requires the main or test channel.");
    }

    if (string.IsNullOrWhiteSpace(builder.Configuration["FootbalForiSettings:BotToken"]))
    {
        throw new InvalidOperationException("Set the test bot token before enabling Telegram publishing.");
    }
}

if (builder.Configuration.GetValue<bool>("Worker:FetchEnabled")
    || builder.Configuration.GetValue<bool>("Worker:PublishingEnabled"))
{
    builder.Services.AddHostedService<FootballforiBackgroundService>();
}

if (builder.Configuration.GetValue<bool>("LiveBoardSettings:Enabled"))
{
    builder.Services.AddSingleton<LiveBoardArchiveService>();
    builder.Services.AddHostedService(services => services.GetRequiredService<LiveBoardArchiveService>());
    builder.Services.AddHostedService<LiveBoardBackgroundService>();
}

var app = builder.Build();
app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = { "debug.html" }
});
app.UseStaticFiles();
app.UseSerilogRequestLogging();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/health"))
    {
        context.Response.Headers.CacheControl = "no-store";
    }

    await next();
});
app.MapGet("/health/live", () => Results.Ok(new { status = "running" }));
app.MapGet("/health/ready", async (TelegramAggregatorContext database, CancellationToken cancellationToken) =>
{
    try
    {
        var pendingMigrations = await database.Database.GetPendingMigrationsAsync(cancellationToken);
        if (pendingMigrations.Any())
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        await database.News.AsNoTracking().Take(1).ToListAsync(cancellationToken);
        return Results.Ok(new { status = "ready" });
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});
app.MapControllers();

try
{
    if (app.Configuration.GetValue<bool>("Database:ApplyMigrations")
        || args.Contains("--migrate-only", StringComparer.Ordinal))
    {
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TelegramAggregatorContext>();
        await database.Database.MigrateAsync();
    }

    if (!args.Contains("--migrate-only", StringComparer.Ordinal))
    {
        await app.RunAsync();
    }
}
finally
{
    await Log.CloseAndFlushAsync();
}
