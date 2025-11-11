namespace TelegramAggregator.WebAPI.Services;

public class FootballforiBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _serviceFactory;
    private readonly ILogger<FootballforiBackgroundService> _logger;
    private readonly Random _random = new();
    public FootballforiBackgroundService(ILogger<FootballforiBackgroundService> logger, IServiceScopeFactory serviceFactory)
    {

        _logger = logger;
        _serviceFactory = serviceFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("⚡ FootballforiBackgroundService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            //var now = DateTime.Now;
            //if (now.Hour >= 1 && now.Hour < 8)
            //{

            //    await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            //    continue;
            //}
            try
            {
                using var scope = _serviceFactory.CreateScope();
                var footbalforiService = scope.ServiceProvider.GetRequiredService<FootballforiService>();

                await footbalforiService.RunAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ خطا در چرخه اصلی FootballforiBackgroundService");
            }

            var delay = TimeSpan.FromMinutes(_random.Next(10, 30));

            _logger.LogInformation("🕒 تاخیر تا چرخه بعدی: {Minutes} دقیقه", delay.TotalMinutes);
            await Task.Delay(delay, stoppingToken);
        }
    }
}
