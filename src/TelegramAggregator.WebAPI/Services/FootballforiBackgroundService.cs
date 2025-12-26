using TelegramAggregator.WebAPI.Data.Repositories;

namespace TelegramAggregator.WebAPI.Services;

public class FootballforiBackgroundService : BackgroundService
{
    private readonly ILogger<FootballforiBackgroundService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Random _rnd = Random.Shared;
    public FootballforiBackgroundService(
        ILogger<FootballforiBackgroundService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FootballforiBackgroundService شروع شد");

        // دو task موازی: یکی برای واکشی، یکی برای انتشار
        var fetchTask = FetchLoopAsync(stoppingToken);
        var publishTask = PublishLoopAsync(stoppingToken);

        await Task.WhenAll(fetchTask, publishTask);
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


    private async Task PublishLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("شروع حلقه انتشار خبر");
        var iranTz = GetIranTimeZone();
        DateOnly? lastExpireRun = null;
        while (!ct.IsCancellationRequested)
        {

            var nowUtc = DateTime.UtcNow;
            var nowIran = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, iranTz);

            bool isWorkingHours = (nowIran.Hour >= 8 && nowIran.Hour <= 23) || nowIran.Hour < 3;


            if (!isWorkingHours)
            {
                _logger.LogDebug(
                    "Not working hours | IranTime={IranTime} | Hour={Hour}",
                    nowIran,
                    nowIran.Hour
                );

                if (nowIran.Hour >= 3 &&
                    lastExpireRun != DateOnly.FromDateTime(nowIran))
                {
                    lastExpireRun = DateOnly.FromDateTime(nowIran);

                    _logger.LogInformation(
                        "Expire job STARTED | IranTime={IranTime}",
                        nowIran
                    );

                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var repo = scope.ServiceProvider.GetRequiredService<INewsRepository>();

                        await repo.ChangeNewsInQueueToExpiredState(ct);

                        _logger.LogInformation(
                            "Expire job FINISHED | IranTime={IranTime}",
                            nowIran
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Expire job ERROR | IranTime={IranTime}",
                            nowIran
                        );

                        lastExpireRun = null; // اجازه retry
                    }
                }
                else
                {
                    _logger.LogDebug(
                        "Expire job skipped | IranTime={IranTime} | Reason=AlreadyRunOrBefore3AM",
                        nowIran
                    );
                }

                await Task.Delay(TimeSpan.FromMinutes(30), ct);
                continue;
            }


            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<FootballforiService>();
                await service.PublishSingleNewsAsync(ct);  // متد جدید که فقط یک خبر بفرسته
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در انتشار خبر");
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }

            //await Task.Delay(TimeSpan.FromSeconds(10), ct);
            await Task.Delay(TimeSpan.FromMinutes(_rnd.Next(20, 40)), ct);

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