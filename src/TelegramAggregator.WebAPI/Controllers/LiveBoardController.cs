using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LiveBoardController : ControllerBase
{
    private readonly IOptions<AppSettings> _settings;

    public LiveBoardController(IOptions<AppSettings> settings)
    {
        _settings = settings;
    }

    [HttpGet("Status")]
    public IActionResult Status()
    {
        var s = _settings.Value.LiveBoardSettings;
        return Ok(new
        {
            enabled = s.Enabled,
            channelId = s.ChannelId,
            messageId = s.MessageId,
            liveRefreshSeconds = s.LiveRefreshSeconds,
            offlineRefreshMinutes = s.OfflineRefreshMinutes
        });
    }
}
