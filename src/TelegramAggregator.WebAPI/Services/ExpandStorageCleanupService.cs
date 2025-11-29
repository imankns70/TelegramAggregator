namespace TelegramAggregator.WebAPI.Services
{
    public class ExpandStorageCleanupService : BackgroundService
    {
        private readonly ILogger<ExpandStorageCleanupService> _logger;
        private readonly IServiceScopeFactory _serviceFactory;

        public ExpandStorageCleanupService(ILogger<ExpandStorageCleanupService> logger, IServiceScopeFactory serviceFactory)
        {
            _logger = logger;
            _serviceFactory = serviceFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<HandleExpandCore>();
                    service.CleanupExpiredItems(); // فقط یکبار اجرا می‌شود
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "خطا در پاک‌سازی ExpandStorage — ادامه می‌دم...");
                }

                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
        }
    }

}
