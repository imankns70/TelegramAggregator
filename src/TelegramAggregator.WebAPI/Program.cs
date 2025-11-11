using Microsoft.Extensions.Options;
using Telegram.Bot;
using TelegramAggregator.Infrastructure;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Load configuration
builder.Configuration.AddJsonFile("footballKeywords.json", optional: true, reloadOnChange: true);
builder.Services.Configure<AppSettings>(builder.Configuration);

// Add controllers and CORS
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// NewsStorage and DuplicateChecker
builder.Services.AddSingleton(sp =>
{
    var settings = sp.GetRequiredService<IOptions<AppSettings>>().Value;
    return new NewsStorage(settings.MaxItems);
});

// 2️⃣ DuplicateChecker (وابسته به NewsStorage)
builder.Services.AddSingleton<DuplicateChecker>(sp =>
{
    var storage = sp.GetRequiredService<NewsStorage>();
    return new DuplicateChecker(storage, 0.88);
});


// Helpers
builder.Services.AddSingleton<HttpHelper>();
builder.Services.AddSingleton<RssParser>();
builder.Services.AddSingleton<HtmlExtractor>();

builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<AppSettings>>().Value;
    var token = settings.BotToken;
    return new Telegram.Bot.TelegramBotClient(token);
});

builder.Services.AddScoped<FootballforiService>();



builder.Services.AddHostedService<FootballforiBackgroundService>();
var app = builder.Build();





// Map endpoints and middleware
app.UseHttpsRedirection();
app.UseHsts();
app.UseCors("AllowAll");
app.MapControllers();


if (app.Environment.IsProduction())
{
    app.Urls.Add("http://0.0.0.0:5000");
}

app.Run();
