using TelegramAggregator.WebAPI.Models.FootbalSettings;

public class BazareRoozService
{
    private readonly ILogger<BazareRoozService> _logger;
    private readonly FootbalForiSettings _footbalForiSettings;

    public BazareRoozService(FootbalForiSettings footbalForiSettings, ILogger<BazareRoozService> logger)
    {
        _footbalForiSettings = footbalForiSettings;
        _logger = logger;
    }
}