using Microsoft.AspNetCore.Mvc;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Controllers
{
    [ApiController]
    [Route("api/debug")]
    public class DebugApiController : ControllerBase
    {
        private readonly NewsStorage _newsStorage;

        public DebugApiController(NewsStorage newsStorage)
        {
            _newsStorage = newsStorage;
        }

        /// <summary>
        /// صفحه اصلی دیباگ — داشبورد زنده با صف‌های کامل و شفاف
        /// </summary>
        [HttpGet("feed")]
        public IActionResult Feed()
        {
            var queueBySource = _newsStorage.GetQueueBySource();
            var result = new
            {
                // صف‌ها به تفکیک منبع — همه خبرها رو نشون میده!
                queues = queueBySource.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Select((item, index) => new
                    {
                        position = index + 1,
                        title = item.Title?.Truncate(90),
                        link = item.Link,
                        pubDate = item.PubDate.ToString("HH:mm"),
                        source = item.SourceName ?? kvp.Key
                    }).ToList()
                ),

                totalInQueue = queueBySource.Sum(x => x.Value.Count),

                // آخرین خبرهای ارسال‌شده
                publishedItems = _newsStorage.GetPublishedItems()
                    .Select((x, i) => new
                    {
                        position = i + 1,
                        source = x.SourceName ?? "نامشخص",
                        title = x.Title?.Truncate(80),
                        link = x.Link,
                        sentAt = x.ProcessedAt.ToString("HH:mm:ss") // چون PublishedAt نداریم، از ProcessedAt استفاده می‌کنیم
                    }).ToList(),

                // آخرین پردازش‌شده‌ها (قبول + رد + در صف)
                recentProcessed = _newsStorage.GetRecentProcessed(60)
                    .Select(x => new
                    {
                        source = x.SourceName ?? "نامشخص",
                        title = x.Title?.Truncate(70),
                        link = x.Link,
                        status = x.IsAccepted ? "قبول" : "رد",
                        reason = x.RejectReason ?? (x.IsAccepted ? "قبول شد" : "فیلتر"),
                        time = x.ProcessedAt.ToString("HH:mm:ss"),
                        publishStatus = x.PublishStatus
                    }).ToList()
            };
            return Ok(result);
        }

        /// <summary>
        /// آمار لحظه‌ای برای داشبورد — سریع، هوشمند و کاملاً استاندارد
        /// </summary>
        [HttpGet("stats")]
        public IActionResult Stats()
        {
            var summary = _newsStorage.GetSummary();
            var now = DateTimeOffset.Now;

            // آخرین ساعت
            var lastHourItems = _newsStorage.GetRecentProcessed(1000)
                .Where(x => x.ProcessedAt > now.AddHours(-1))
                .ToList();

            // امروز
            var todayAccepted = _newsStorage.GetRecentProcessed(5000)
                .Count(x => x.IsAccepted && x.ProcessedAt.Date == now.Date);

            var result = new
            {
                summary.TotalProcessed,
                summary.AcceptedCount,
                summary.RejectedCount,
                summary.PublishedCount,
                summary.InQueueCount,
                summary.AcceptanceRate,
                summary.SystemStatus,
                summary.StatusColor,
                summary.QueueDetails,

                // آمار آخرین ساعت
                LastHour = new
                {
                    Total = lastHourItems.Count,
                    Accepted = lastHourItems.Count(x => x.IsAccepted),
                    Rejected = lastHourItems.Count(x => !x.IsAccepted),
                    AcceptanceRate = lastHourItems.Count > 0
                        ? Math.Round((double)lastHourItems.Count(x => x.IsAccepted) * 100 / lastHourItems.Count, 1)
                        : 0.0
                },

                // آمار امروز
                Today = new
                {
                    Accepted = todayAccepted
                },

                // رنگ داشبورد
                DashboardStatusColor = summary.StatusColor
            };

            return Ok(result);
        }
    }
}