using Microsoft.Extensions.Options;
using TelegramAggregator.Core;
using TelegramAggregator.Infrastructure;
using TelegramAggregator.WebAPI.Core;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Steps;

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

// Pipeline registry
builder.Services.AddSingleton<PipelineRegistry>();
builder.Services.AddSingleton<PipelineRunner>();

// Helpers
builder.Services.AddSingleton<HttpHelper>();
builder.Services.AddSingleton<RssParser>();
builder.Services.AddSingleton<HtmlExtractor>();

// Steps
builder.Services.AddTransient<FetchAndParseRssStep>();
builder.Services.AddTransient<FilterStep>();
builder.Services.AddTransient<FetchFullContentStep>();
builder.Services.AddTransient<PublishStep>();

var app = builder.Build();
var serviceProvider = app.Services;

// Register pipelines
var registry = serviceProvider.GetRequiredService<PipelineRegistry>();

registry.RegisterPipeline("RssPipeline", new IPipelineStep[]
{
    serviceProvider.GetRequiredService<FetchAndParseRssStep>(),
    serviceProvider.GetRequiredService<FilterStep>(),
    serviceProvider.GetRequiredService<FetchFullContentStep>(),
    serviceProvider.GetRequiredService<PublishStep>()
});

registry.RegisterPipeline("AdminPipeline", new IPipelineStep[]
{
    // serviceProvider.GetRequiredService<FetchFullContentStep>(),
    // serviceProvider.GetRequiredService<PublishStep>()
});

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
