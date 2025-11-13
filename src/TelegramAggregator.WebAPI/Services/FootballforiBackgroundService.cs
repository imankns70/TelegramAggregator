namespace TelegramAggregator.WebAPI.Services;

public class FootballforiBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _serviceFactory;
    private readonly ILogger<FootballforiBackgroundService> _logger;
    private readonly Random _random = new();
    private bool _isRunning = false;

    public FootballforiBackgroundService(
        ILogger<FootballforiBackgroundService> logger,
        IServiceScopeFactory serviceFactory)
    {
        _logger = logger;
        _serviceFactory = serviceFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("⚡ FootballforiBackgroundService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;

            // 💤 حالت شبانه (۱ تا ۸ صبح)
            if (now.Hour >= 1 && now.Hour < 8)
            {
                _logger.LogInformation("🌙 ساعت {Hour}: در حالت شبانه هستیم، بررسی بعدی تا ۳۰ دقیقه دیگر", now.Hour);
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
                continue;
            }

            // جلوگیری از اجرای هم‌زمان
            if (_isRunning)
            {
                _logger.LogWarning("⚠️ اجرای قبلی هنوز به پایان نرسیده است، اجرای جدید لغو شد.");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                continue;
            }

            _isRunning = true;
            var startTime = DateTime.UtcNow;

            try
            {
                _logger.LogInformation("🚀 شروع اجرای جدید FootballforiService در {Time}", DateTime.Now);

                using var scope = _serviceFactory.CreateScope();
                var footbalforiService = scope.ServiceProvider.GetRequiredService<FootballforiService>();

                await footbalforiService.RunAsync(stoppingToken);

                _logger.LogInformation("✅ اجرای FootballforiService با موفقیت به پایان رسید.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ خطا در چرخه اصلی FootballforiBackgroundService");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            finally
            {
                _isRunning = false;
            }

            // 🕒 محاسبه فاصله بین دو اجرای متوالی (از شروع تا شروع)
            var elapsed = DateTime.UtcNow - startTime;
            var randomDelay = TimeSpan.FromMinutes(_random.Next(10, 20));
            var delay = randomDelay - elapsed;
            if (delay < TimeSpan.Zero)
                delay = TimeSpan.Zero;

            _logger.LogInformation("🕒 تاخیر تا چرخه بعدی: {Minutes} دقیقه (زمان اجرا: {Elapsed} دقیقه)",
                delay.TotalMinutes.ToString("0.0"), elapsed.TotalMinutes.ToString("0.0"));

            await Task.Delay(delay, stoppingToken);
        }
    }
}
