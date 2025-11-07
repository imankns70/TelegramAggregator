
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("footballKeywords.json", optional: true, reloadOnChange: true);
builder.Services.Configure<AppSettings>(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();


builder.Services.AddSingleton<AggregatorService>();
builder.Services.AddHostedService<AggregatorService>();
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


app.UseHttpsRedirection();
app.UseHsts();

app.UseCors("AllowAll");

app.MapControllers();
if (app.Environment.IsProduction())
{
    app.Urls.Add("http://0.0.0.0:5000");

}

app.Run();
