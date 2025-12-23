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

        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.Now;
            if (now.Hour >= 1 && now.Hour < 8)
            {
                await Task.Delay(TimeSpan.FromMinutes(30), ct);
                continue;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<FootballforiService>();
                await service.FetchAndFilterNewsAndAddToQueueAsync(ct);  // فقط این متد رو صدا بزن
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
        _logger.LogInformation("شروع حلقه انتشار خبر — کنترل‌شده و ایمن");

        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.Now;
            bool isWorkingHours = now.Hour >= 8 || now.Hour < 3;

            if (!isWorkingHours)
            {
                // فقط یک بار در ساعت ۳ صبح صف‌ها رو خالی کن
                if (now.Hour == 3 && now.Minute < 5)
                {
                    _logger.LogInformation("ساعت ۳ صبح — پاکسازی صف برای شروع روز جدید");
                    using var scope = _scopeFactory.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<INewsRepository>();
                    await repo.ChangeNewsInQueueToExpiredState(ct);
                }

                await Task.Delay(TimeSpan.FromMinutes(30), ct);
                continue;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<FootballforiService>();

                bool published = await service.PublishSingleNewsAsync(ct);

                if (published)
                {
                    // تأخیر تصادفی ۲۰ تا ۴۰ دقیقه بعد از ارسال موفق
                    int delayMinutes = _rnd.Next(20, 41);
                    _logger.LogInformation("ارسال موفق — تأخیر {DelayMinutes} دقیقه‌ای اعمال شد.", delayMinutes);
                    await Task.Delay(TimeSpan.FromMinutes(delayMinutes), ct);
                }
                else
                {
                    // اگر خبری نبود، کمی صبر کن و دوباره چک کن
                    await Task.Delay(TimeSpan.FromMinutes(_rnd.Next(3, 7)), ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطای غیرمنتظره در حلقه انتشار");
                await Task.Delay(TimeSpan.FromMinutes(5), ct); // در خطا صبر طولانی‌تر
            }
        }
    }
}