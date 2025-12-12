using AngleSharp.Dom;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using Telegram.Bot;
using TelegramAggregator.WebAPI.Data;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Infrastructure.Storage;
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Services;
using TelegramAggregator.WebAPI.Services.Dashboard;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("footbalScoring.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();
// Bind تنظیمات
builder.Services.Configure<AppSettings>(builder.Configuration);

builder.Services.AddDbContext<TelegramAggregatorContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddRepositories();
// ------------------- سرویس‌ها -------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// استاتیک فایل‌ها (برای debug.html)
builder.Services.AddDirectoryBrowser(); // اختیاری: برای مرور فایل‌ها در wwwroot
builder.Logging.AddConsole();

// ------------------- سرویس‌های اصلی -------------------

// NewsStorage با تنظیمات دقیق
builder.Services.AddSingleton<IDebugStore, DebugStore>();

builder.Services.AddSingleton<INewsStorage, NewsStorage>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<AppSettings>>().Value;
    return new NewsStorage(queueCapacityPerSource: settings.FootbalForiSettings.QueueCapacityPerSource,
        maxSeenKeys: settings.FootbalForiSettings.MaxSeenKeys
    );
});



builder.Services.AddHttpClient<IHttpHelperService, HttpHelperService>("RssHttpClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);

    // وانمود می‌کنیم یک مرورگر واقعی هستیم:
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");

    // جلوگیری از کش سمت CDN یا سرور
    client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
    {
        NoCache = true,
        NoStore = true,
        MustRevalidate = true
    };

    client.DefaultRequestHeaders.Pragma.TryParseAdd("no-cache");
    client.DefaultRequestHeaders.IfNoneMatch.Clear();
});


builder.Services.AddSingleton<IDebugStore, DebugStore>();
builder.Services.AddSingleton<IFootballForiDashboardService, FootballForiDashboardService>();
// سرویس‌های Scoped
builder.Services.AddScoped<IRssParserService, RssParserService>();
builder.Services.AddScoped<IImageExtractorService, ImageExtractorService>();
builder.Services.AddScoped<IFullDecriptionExtractorService, FullDecriptionExtractorService>();
builder.Services.AddScoped<IFootball360ScraperService, Football360ScraperService>();




// Telegram Bot
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var token = sp.GetRequiredService<IOptions<AppSettings>>().Value.FootbalForiSettings.BotToken
                 ?? throw new InvalidOperationException("BotToken تنظیم نشده است!");
    return new TelegramBotClient(token);

});


// سرویس اصلی + سرویس پس‌زمینه
builder.Services.AddScoped<FootballforiService>();
builder.Services.AddHostedService<FootballforiBackgroundService>(); // این خودش Publish رو یک بار استارت می‌کنه

// ------------------- اپلیکیشن -------------------
var app = builder.Build();


// Middleware ها
app.UseHttpsRedirection();
app.UseHsts();
app.UseStaticFiles();        // مهم: برای debug.html و wwwroot
app.UseRouting();
app.UseCors("AllowAll");



app.MapControllers();

// در پروداکشن روی همه آی‌پی‌ها گوش بده
if (app.Environment.IsProduction())
{
    app.Urls.Add("http://0.0.0.0:5000");
}

// ------------------- استارت -------------------
app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine("ربات فوتبالفوری با موفقیت استارت شد!");
    Console.WriteLine($"محیط: {app.Environment.EnvironmentName}");
    Console.WriteLine($"دسترسی به داشبورد: http://localhost:5000/debug.html");
});

app.Run();