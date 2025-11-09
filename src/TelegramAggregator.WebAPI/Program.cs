using Microsoft.Extensions.Options;
using TelegramAggregator.Core;
using TelegramAggregator.WebAPI.Core;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Steps;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("footballKeywords.json", optional: true, reloadOnChange: true);
builder.Services.Configure<AppSettings>(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSingleton(sp =>
{
    var setting = sp.GetRequiredService<IOptions<AppSettings>>().Value;
    return new DuplicateChecker(setting.MaxItems);
});
builder.Services.AddSingleton<PipelineRegistry>();
builder.Services.AddSingleton<PipelineRunner>();

builder.Services.AddSingleton<HttpHelper>();


builder.Services.AddSingleton<PipelineRegistry>();
builder.Services.AddSingleton<PipelineRunner>();

builder.Services.AddTransient<FetchRssStep>();
builder.Services.AddTransient<ParseFeedStep>();
builder.Services.AddTransient<FilterStep>();
builder.Services.AddTransient<FetchFullContentStep>();
builder.Services.AddTransient<UpdateQueueStep>();
builder.Services.AddTransient<PublishStep>();



//builder.Services.AddSingleton<AggregatorService>();
//builder.Services.AddHostedService<AggregatorService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();
var serviceProvider = app.Services;
var registry = serviceProvider.GetRequiredService<PipelineRegistry>();

// پایپلاین RSS فقط برای BackgroundService
registry.RegisterPipeline("RssPipeline", new IPipelineStep[]
{
    serviceProvider.GetRequiredService<FetchRssStep>(),
    serviceProvider.GetRequiredService<ParseFeedStep>(),
    serviceProvider.GetRequiredService<FilterStep>(),
    serviceProvider.GetRequiredService<FetchFullContentStep>(),
    serviceProvider.GetRequiredService<UpdateQueueStep>(),
    serviceProvider.GetRequiredService<PublishStep>()
});

// پایپلاین Admin که توسط Controller قابل اجراست
registry.RegisterPipeline("AdminPipeline", new IPipelineStep[]
{
    //serviceProvider.GetRequiredService<FetchFullContentStep>(),
    //serviceProvider.GetRequiredService<UpdateQueueStep>(),
    //serviceProvider.GetRequiredService<PublishStep>()
});

app.UseHttpsRedirection();
app.UseHsts();

app.UseCors("AllowAll");

app.MapControllers();
if (app.Environment.IsProduction())
{
    app.Urls.Add("http://0.0.0.0:5000");

}

app.Run();
