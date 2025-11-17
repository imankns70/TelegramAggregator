using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using Telegram.Bot;
using TelegramAggregator.Infrastructure;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Services;


Console.OutputEncoding = Encoding.UTF8; // مهم
Console.InputEncoding = Encoding.UTF8;

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
    return new NewsStorage(settings.MaxItemsPerSource,settings.PublishQueueCapacity);
});


builder.Services.AddSingleton<LineupFilter>(sp =>
{
    var keywords = sp.GetRequiredService<IOptions<AppSettings>>().Value.LineupKeywords;
    return new LineupFilter(keywords);
});

// HttpClient Factory
builder.Services.AddHttpClient<HttpHelper>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "FootballforiBot/1.0");
});

builder.Services.AddHttpClient("HtmlExtractor", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; FootballforiBot/1.0)");
});
builder.Services.AddHttpClient("RssHttpClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = DecompressionMethods.All,
    AllowAutoRedirect = true
});

builder.Services.AddScoped<IHttpHelper, HttpHelper>();

builder.Services.AddScoped<IRssParser, RssParser>();
builder.Services.AddScoped<IHtmlExtractor, HtmlExtractor>();

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
