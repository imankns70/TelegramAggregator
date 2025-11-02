using Microsoft.AspNetCore.Mvc;
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Services;

namespace TelegramAggregator.WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AggregatorController : ControllerBase
    {
        private readonly AggregatorService _service;
        private readonly IConfiguration _config;
        private readonly ILogger<AggregatorController> _logger;
        public AggregatorController(AggregatorService service, IConfiguration config,
            ILogger<AggregatorController> logger)
        {
            _service = service;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// دستی اجرا کن (برای ادمین‌ها)
        /// </summary>
        [HttpPost("RunNow")]
        public async Task<ActionResult> RunNow()
        {
            //if (!IsValidApiKey(apiKey))
            //    return Unauthorized("API Key نامعتبر است");

            _logger.LogInformation("درخواست دستی اجرا از {IP}", GetClientIp());

            try
            {
                await _service.ProcessAllChannels(CancellationToken.None);
                return Ok(new { message = "خبرها با موفقیت ارسال شدند!", timestamp = DateTime.Now });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در اجرای دستی");
                return StatusCode(500, new { error = "خطا در ارسال خبرها" });
            }
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
