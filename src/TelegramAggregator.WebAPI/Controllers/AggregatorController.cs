using Microsoft.AspNetCore.Mvc;
using Telegram.Bot.Types;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models.FootbalSettings;
using TelegramAggregator.WebAPI.Services;

namespace TelegramAggregator.WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AggregatorController : ControllerBase
    {


    
        private readonly IConfiguration _config;
        private readonly ILogger<AggregatorController> _logger;
        public AggregatorController(IConfiguration config, ILogger<AggregatorController> logger)
        {

            _config = config;
            _logger = logger; 
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

            var channels = _config.GetSection("Channels").Get<List<FootbalForiSettings>>() ?? new();
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


            var channels = _config.GetSection("Channels").Get<List<FootbalForiSettings>>() ?? new();
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
