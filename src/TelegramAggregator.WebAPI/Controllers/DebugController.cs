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
                recentProcessed = _newsStorage.GetRecentProcessed(80)
                .OrderByDescending(x => x.ProcessedAt)
    .Select(x => new
    {
        source = x.SourceName ?? "نامشخص",
        title = x.Title?.Truncate(80) ?? "بدون عنوان",
        link = x.Link,
        time = x.ProcessedAt.ToString("HH:mm:ss"),

        // این دو خط کلید پیروزی هستن!
        status = GetItemStatus(x),      // درست و منطقی
        reason = GetItemReason(x),      // درست و صادقانه
    })
 
    .Take(60)
    .ToList()
            };
       
            return Ok(result);
        }

        /// <summary>
        /// آمار لحظه‌ای برای داشبورد — سریع، هوشمند و کاملاً استاندارد
        /// </summary>
        [HttpGet("stats")]
        public IActionResult Stats()
        {
            var now = DateTimeOffset.Now;

            // تابع کمکی: آیا خبر واقعاً قبول شده حساب میشه؟
            bool IsActuallyAccepted(DebugFeedItem x)
            {
                return x.PublishStatus is "جدید_واکشی_شده" or "در صف انتشار" || x.IsAccepted;
            }

            // همه خبرهای اخیر (کافی برای محاسبه همه چیز)
            var recentAll = _newsStorage.GetRecentProcessed(10000);

            // آخرین ساعت
            var lastHourItems = recentAll
                .Where(x => x.ProcessedAt > now.AddHours(-1))
                .ToList();

            // امروز
            var todayItems = recentAll
                .Where(x => x.ProcessedAt.Date == now.Date)
                .ToList();

            // محاسبه آمار واقعی و صادقانه
            var lastHourAccepted = lastHourItems.Count(IsActuallyAccepted);
            var lastHourRejected = lastHourItems.Count - lastHourAccepted;

            var todayAccepted = todayItems.Count(IsActuallyAccepted);

            // از GetSummary() استفاده می‌کنیم که الان خودش هم صادقه!
            var summary = _newsStorage.GetSummary();

            var result = new
            {
                // آمار کلی — ۱۰۰٪ صادقانه (از GetSummary که درست کردیم)
                summary.TotalProcessed,
                summary.AcceptedCount,
                summary.RejectedCount,
                summary.PublishedCount,
                summary.InQueueCount,
                summary.AcceptanceRate,
                summary.SystemStatus,
                summary.StatusColor,
                summary.QueueDetails,

                // آمار آخرین ساعت — کاملاً واقعی و هماهنگ
                LastHour = new
                {
                    Total = lastHourItems.Count,
                    Accepted = lastHourAccepted,
                    Rejected = lastHourRejected,
                    AcceptanceRate = lastHourItems.Count > 0
                        ? Math.Round((double)lastHourAccepted * 100 / lastHourItems.Count, 1)
                        : 0.0
                },

                // آمار امروز — کامل و صادقانه
                Today = new
                {
                    Total = todayItems.Count,
                    Accepted = todayAccepted,
                    Rejected = todayItems.Count - todayAccepted,
                    AcceptanceRate = todayItems.Count > 0
                        ? Math.Round((double)todayAccepted * 100 / todayItems.Count, 1)
                        : 0.0
                },

                // رنگ داشبورد — بر اساس آخرین ساعت (واقعی‌تر)
                DashboardStatusColor = lastHourAccepted >= lastHourItems.Count * 0.90 ? "green" :
                                       lastHourAccepted >= lastHourItems.Count * 0.70 ? "yellow" : "red"
            };

            return Ok(result);
        }
        [HttpGet("duplicates")]
        public IActionResult CheckDuplicates()
        {
            var duplicates = _newsStorage._debugItems.Values
                .Where(x => !string.IsNullOrEmpty(x.Link) || !string.IsNullOrEmpty(x.Title))
                .GroupBy(x => x.Link ?? $"{x.Title?.Trim()}-{x.SourceName}")
                .Where(g => g.Count() > 1)
                .Select(g => new
                {
                    Key = g.Key,
                    Count = g.Count(),
                    Items = g.OrderByDescending(x => x.ProcessedAt)
                             .Select(x => new
                             {
                                 x.Title,
                                 x.SourceName,
                                 x.Link,
                                 Status = x.PublishStatus,
                                 //Reason = x.RejectReason ?? x.QueueReason ?? "—",
                                 Time = x.ProcessedAt.ToString("HH:mm:ss")
                             })
                             .ToList()
                })
                .OrderByDescending(x => x.Count)
                .ToList();

            var totalDuplicates = duplicates.Sum(x => x.Count - 1); // هر گروه چند تا اضافه داره
            var totalGroups = duplicates.Count;

            return Ok(new
            {
                TotalDuplicateGroups = totalGroups,
                TotalExtraItems = totalDuplicates,
                Message = totalGroups == 0
                    ? "عالی! هیچ خبر تکراری وجود ندارد — سیستم کاملاً تمیزه"
                    : $"هشدار: {totalGroups} گروه تکراری پیدا شد ({totalDuplicates} خبر اضافه)",
                Duplicates = duplicates
            });
        }
        private static string GetItemStatus(DebugFeedItem x)
        {
            // اولویت مطلق: PublishStatus
            return x.PublishStatus switch
            {
                "ارسال شد" => "ارسال شد",
                "در صف انتشار" => "قبول شده",
                "جدید_واکشی_شده" => "قبول شده",  // این مهمه!
                _ when x.IsAccepted => "قبول شده",
                _ => "رد شد"
            };
        }

        private static string GetItemReason(DebugFeedItem x)
        {
            return x.PublishStatus switch
            {
                "جدید_واکشی_شده" => "در حال پردازش — تازه رسید",
                "در صف انتشار" => "در صف ارسال",
                "ارسال شد" => "ارسال موفق",
                _ => x.RejectReason ?? "در حال بررسی"
            };
        }
    }
}