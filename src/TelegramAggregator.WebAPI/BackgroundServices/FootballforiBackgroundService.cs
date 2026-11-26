using TelegramAggregator.WebAPI.Data.Repositories;
using TelegramAggregator.WebAPI.Services;

namespace TelegramAggregator.WebAPI.BackgroundServices;

public class FootballforiBackgroundService : BackgroundService
{
    private const int MinPublishIntervalMinutes = 20;
    private const int MaxPublishIntervalMinutes = 40;
    private readonly ILogger<FootballforiBackgroundService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    public FootballforiBackgroundService(
        ILogger<FootballforiBackgroundService> logger,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FootballforiBackgroundService شروع شد");

        // دو task موازی: یکی برای واکشی، یکی برای انتشار
        var fetchTask = _configuration.GetValue<bool>("Worker:FetchEnabled")
            ? FetchLoopAsync(stoppingToken)
            : Task.CompletedTask;
        var publishTask = _configuration.GetValue<bool>("Worker:PublishingEnabled")
            ? PublishLoopAsync(stoppingToken)
            : Task.CompletedTask;

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
        DateTimeOffset? nextPublishAt = null;
        while (!ct.IsCancellationRequested)
        {

            var nowUtc = DateTime.UtcNow;
            var nowIran = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, iranTz);

            bool isWorkingHours = nowIran.Hour >= 8 || nowIran.Hour < 1;


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

                    _logger.LogDebug(
                        "Expire job STARTED | IranTime={IranTime}",
                        nowIran
                    );

                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var repo = scope.ServiceProvider.GetRequiredService<INewsRepository>();

                        await repo.ChangeNewsInQueueToExpiredState(ct);

                        _logger.LogDebug(
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


            var delay = TimeSpan.FromMinutes(1);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                if (nextPublishAt is null)
                {
                    var repo = scope.ServiceProvider.GetRequiredService<INewsRepository>();
                    var lastSent = (await repo.GetLastNews(ct))?.SentDate;
                    nextPublishAt = lastSent?.AddMinutes(MinPublishIntervalMinutes) ?? DateTimeOffset.MinValue;
                }

                var remaining = nextPublishAt.Value - DateTimeOffset.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    delay = remaining;
                }
                else
                {
                    var service = scope.ServiceProvider.GetRequiredService<FootballforiService>();
                    if (await service.PublishSingleNewsAsync(ct))
                    {
                        var interval = Random.Shared.Next(MinPublishIntervalMinutes, MaxPublishIntervalMinutes + 1);
                        delay = TimeSpan.FromMinutes(interval);
                        nextPublishAt = DateTimeOffset.UtcNow + delay;
                        _logger.LogDebug("زمان خواب تا ارسال بعدی {Minutes} دقیقه", interval);
                    }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "خطا در انتشار خبر");
            }

            await Task.Delay(delay, ct);
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
