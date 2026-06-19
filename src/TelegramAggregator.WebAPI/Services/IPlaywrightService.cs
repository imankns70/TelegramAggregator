using Microsoft.Playwright;

namespace TelegramAggregator.WebAPI.Services;

public interface IPlaywrightService
{
    Task<IBrowserContext> CreateContextAsync(CancellationToken ct = default);
}
