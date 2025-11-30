namespace TelegramAggregator.WebAPI.Services
{
    public class ExpandStorageCleanupService : BackgroundService
    {
        private readonly ILogger<ExpandStorageCleanupService> _logger;
        private readonly HandleExpandCore _expandCore;

        public ExpandStorageCleanupService(
            ILogger<ExpandStorageCleanupService> logger,
            HandleExpandCore expandCore)
        {
            _logger = logger;
            _expandCore = expandCore;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _expandCore.CleanupExpiredItems();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "خطا در پاک‌سازی ExpandStorage");
                }

                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
        }
    }

}
