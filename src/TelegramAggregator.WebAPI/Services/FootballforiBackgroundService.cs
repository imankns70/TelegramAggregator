namespace TelegramAggregator.WebAPI.Services;

public class FootballforiBackgroundService : BackgroundService
{
    private readonly ILogger<FootballforiBackgroundService> _logger;
    private readonly IServiceScopeFactory _serviceFactory;    

    public FootballforiBackgroundService(
        ILogger<FootballforiBackgroundService> logger,
        IServiceScopeFactory serviceFactory) 
    {
        _logger = logger;
        _serviceFactory = serviceFactory;
        
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FootballforiBackgroundService شروع شد");

        using var scope = _serviceFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<FootballforiService>();
        await service.StartAsync(stoppingToken);
    }
}