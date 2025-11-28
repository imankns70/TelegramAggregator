using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using Telegram.Bot;
using TelegramAggregator.Infrastructure;
using TelegramAggregator.Services;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Services;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// ------------------- تنظیمات و کانفیگ -------------------
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("footballKeywords.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Bind تنظیمات
builder.Services.Configure<AppSettings>(builder.Configuration);

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
builder.Services.AddSingleton<NewsStorage>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<AppSettings>>().Value;
    return new NewsStorage(queueCapacityPerSource: settings.QueueCapacityPerSource,
        maxSeenKeys: settings.MaxSeenKeys
    );
});
 

builder.Services.AddHttpClient(nameof(ImageExtractor), client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; FootballforiBot/1.0)");
});

builder.Services.AddHttpClient(nameof(FullDecriptionExtractor), client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; FootballforiBot/1.0)");
});

builder.Services.AddHttpClient("RssHttpClient", client =>
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
    client.DefaultRequestHeaders.IfNoneMatch.Clear(); // حذف ETag
});


// سرویس‌های Scoped
builder.Services.AddScoped<IHttpHelper, HttpHelper>();
builder.Services.AddScoped<IRssParser, RssParser>();
builder.Services.AddScoped<IImageExtractor, ImageExtractor>();
builder.Services.AddScoped<IFullDecriptionExtractor, FullDecriptionExtractor>();

// Telegram Bot
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var token = sp.GetRequiredService<IOptions<AppSettings>>().Value.BotToken
                 ?? throw new InvalidOperationException("BotToken تنظیم نشده است!");
    return new TelegramBotClient(token);
});

builder.Services.AddHostedService<TelegramUpdateService>(); // این خط طلاییه!



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