using Microsoft.AspNetCore.Mvc;
using Telegram.Bot.Types;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Services;

namespace TelegramAggregator.WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AggregatorController : ControllerBase
    {


        private readonly HandleExpandCore _handleExpandCore;
        private readonly IConfiguration _config;
        private readonly ILogger<AggregatorController> _logger;
        public AggregatorController(IConfiguration config, ILogger<AggregatorController> logger,
        HandleExpandCore handleExpandCore)
        {

            _config = config;
            _logger = logger;
            _handleExpandCore = handleExpandCore;
        }

        [HttpPost("update")]
        public async Task<IActionResult> OnUpdate([FromBody] Update update, CancellationToken ct)
        {
            try
            {
                if (update.CallbackQuery?.Data?.StartsWith("exp_") == true)
                {
                    await _handleExpandCore.HandleExpand(update.CallbackQuery, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled update error");
            }

            return Ok();
        }

        /// <summary>
        /// وضعیت سیستم
        /// </summary>
        [HttpGet]
        [Route("Status")]
        public ActionResult Status()
        {
            //if (!IsValidApiKey(apiKey))
            //    return Unauthorized("API Key نامعتبر است");

            var channels = _config.GetSection("Channels").Get<List<ChannelConfig>>() ?? new();
            var lastRun = DateTime.Now; // بعداً با لاگ واقعی جایگزین کن

            return Ok(new
            {
                status = "در حال اجرا",
                lastRun,
                pollIntervalMinutes = _config["PollIntervalMinutes"],
                totalChannels = channels.Count,
                channels = channels.Select(c => new { c.Name, c.ChannelId, c.AdminUsername })
            });
        }
      

        /// <summary>
        /// لیست کانال‌ها
        /// </summary>
        // GetChannels([FromHeader(Name = "X-API-Key")] string? apiKey)
        [HttpGet("Channels")]
        public ActionResult GetChannels()
        {


            var channels = _config.GetSection("Channels").Get<List<ChannelConfig>>() ?? new();
            return Ok(channels.Select(c => new
            {
                c.Name,
                c.ChannelId,
                c.AdminUsername,
                sourceCount = c.Sources.Count
            }));
        }

        private bool IsValidApiKey(string? key)
        {
            var validKey = _config["ApiKey"];
            return !string.IsNullOrEmpty(validKey) && key == validKey;
        }

        private string GetClientIp()
        {
            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "نامشخص";
        }

    }
}
