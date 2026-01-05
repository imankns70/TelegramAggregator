using TelegramAggregator.WebAPI.Data.Repositories;
using TelegramAggregator.WebAPI.Services;

namespace TelegramAggregator.WebAPI.BackgroundServices;

public class BazareRoozBackgroundService : BackgroundService
{
    private readonly ILogger<BazareRoozBackgroundService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Random _rnd = Random.Shared;
    public BazareRoozBackgroundService(
        ILogger<BazareRoozBackgroundService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FootballforiBackgroundService شروع شد");

        // دو task موازی: یکی برای واکشی، یکی برای انتشار
        await FetchLoopAsync(stoppingToken);


    }

    private async Task FetchLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("شروع حلقه واکشی RSS");

        var iranTz = GetIranTimeZone();

        while (!ct.IsCancellationRequested)
        {
            var nowIran = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, iranTz);

            // توقف واکشی از 01:00 تا 07:59 به وقت ایران
            if (nowIran.Hour >= 1 && nowIran.Hour < 8)
            {
                await Task.Delay(TimeSpan.FromMinutes(30), ct);
                continue;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<FootballforiService>();
                await service.FetchAndFilterNewsAndAddToQueueAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در واکشی RSS");
            }

            await Task.Delay(TimeSpan.FromMinutes(20), ct);
        }
    }


        private static TimeZoneInfo GetIranTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        }
    }

}