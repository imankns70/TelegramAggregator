using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AggregatorController : ControllerBase
{
    private readonly AppSettings _settings;
    private readonly IConfiguration _configuration;

    public AggregatorController(IOptions<AppSettings> settings, IConfiguration configuration)
    {
        _settings = settings.Value;
        _configuration = configuration;
    }

    [HttpGet("Status")]
    public IActionResult Status()
    {
        return Ok(new
        {
            status = "running",
            fetchEnabled = _configuration.GetValue<bool>("Worker:FetchEnabled"),
            publishingEnabled = _configuration.GetValue<bool>("Worker:PublishingEnabled"),
            readinessUrl = "/health/ready",
            sourceCount = _settings.FootbalForiSettings.Sources.Count + 1
        });
    }

    [HttpGet("Channels")]
    public IActionResult GetChannels()
    {
        var channel = _settings.FootbalForiSettings;
        return Ok(new[]
        {
            new
            {
                channel.Name,
                channel.ChannelId,
                channel.AdminUsername,
                sourceCount = channel.Sources.Count + 1
            }
        });
    }
}
