using Microsoft.Playwright;

namespace TelegramAggregator.WebAPI.Services;

public class PlaywrightService : IPlaywrightService, IAsyncDisposable
{
    private readonly Lazy<Task<IPlaywright>> _playwright = new(() => Microsoft.Playwright.Playwright.CreateAsync());
    private readonly Lazy<Task<IBrowser>> _browser;

    public PlaywrightService()
    {
        _browser = new(async () =>
        {
            var pw = await _playwright.Value;
            return await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = new[] { "--no-sandbox", "--disable-setuid-sandbox" }
            });
        });
    }

    public async Task<IBrowserContext> CreateContextAsync(CancellationToken ct = default)
    {
        var browser = await _browser.Value;
        return await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 }
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser.IsValueCreated)
        {
            var browser = await _browser.Value;
            await browser.CloseAsync();
        }
        if (_playwright.IsValueCreated)
        {
            var pw = await _playwright.Value;
            pw.Dispose();
        }
    }
}
