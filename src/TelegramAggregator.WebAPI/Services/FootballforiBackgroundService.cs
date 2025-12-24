using TelegramAggregator.WebAPI.Data.Repositories;

namespace TelegramAggregator.WebAPI.Services;

public class FootballforiBackgroundService : BackgroundService
{
    private readonly ILogger<FootballforiBackgroundService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

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
        await FetchLoopAsync(stoppingToken);
        //var fetchTask = FetchLoopAsync(stoppingToken);
        //var publishTask = PublishLoopAsync(stoppingToken);

        //await Task.WhenAll(fetchTask, publishTask);
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

            await Task.Delay(TimeSpan.FromMinutes(10), ct);
        }
    }

    private async Task PublishLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("شروع حلقه انتشار خبر");

        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.Now;
            bool isWorkingHours = now.Hour >= 8 || now.Hour < 3;

            if (!isWorkingHours)
            {
                if (now.Hour == 3 && now.Minute < 5)
                {
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
                await service.PublishSingleNewsAsync(ct);  // متد جدید که فقط یک خبر بفرسته
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در انتشار خبر");
            }

            await Task.Delay(3000, ct); // یا delay تصادفی
        }
    }
}