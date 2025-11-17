using Microsoft.AspNetCore.Mvc;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DebugController : ControllerBase
    {
        private readonly DebugStorage _debugStorage;
        private readonly NewsStorage _newsStorage;

        public DebugController(DebugStorage debugStorage, NewsStorage newsStorage)
        {
            _debugStorage = debugStorage;
            _newsStorage = newsStorage;
        }

        // API اصلی: همه خبرهای پردازش‌شده (قبول و رد)
        [HttpGet("feed")]
        public IActionResult Feed([FromQuery] string? source = null, [FromQuery] bool? accepted = null)
        {
            var result = _debugStorage.GetRecentItems()
                .Where(x => string.IsNullOrEmpty(source) ||
                            x.SourceName?.Contains(source!, StringComparison.OrdinalIgnoreCase) == true)
                .Where(x => !accepted.HasValue || x.IsAccepted == accepted.Value)
                .OrderByDescending(x => x.ProcessedAt)
                .Take(100)
                .Select(x => new
                {
                    زمان = x.ProcessedAt.ToString("HH:mm:ss"),
                    منبع = x.SourceName ?? "نامشخص",
                    عنوان = x.Title,
                    لینک = x.Link,
                    وضعیت = x.IsAccepted ? "قبول شد → صف" : "رد شد",
                    دلیل = x.RejectReason ?? "—",
                    تاریخ_خبر = x.PubDate.ToString("MM/dd HH:mm")
                })
                .ToList();

            return Ok(result);
        }
        // آمار ساده بدون نیاز به SentStorage
        [HttpGet("stats")]
        public IActionResult Stats()
        {
            var all = _debugStorage.GetRecentItems();
            var lastHour = DateTimeOffset.Now.AddHours(-1);
            var today = DateTimeOffset.Now.Date;

            var recent = all.Where(x => x.ProcessedAt >= lastHour).ToList();

            return Ok(new
            {
                زمان_سرور = DateTimeOffset.Now.ToString("yyyy/MM/dd HH:mm:ss"),
                صف_فعلی = _newsStorage.GetCurrentPublishQueue().Count,

                در_ساعت_اخیر = new
                {
                    کل_خبرها = recent.Count,
                    قبول_شده = recent.Count(x => x.IsAccepted),
                    رد_شده = recent.Count(x => !x.IsAccepted),
                    درصد_قبولی = recent.Any()
                        ? Math.Round(100.0 * recent.Count(x => x.IsAccepted) / recent.Count, 1)
                        : 0
                },

                امروز = new
                {
                    کل_خبرها = all.Count(x => x.ProcessedAt.Date == today),
                    قبول_شده = all.Count(x => x.ProcessedAt.Date == today && x.IsAccepted)
                }
            });
        }
    }
}
