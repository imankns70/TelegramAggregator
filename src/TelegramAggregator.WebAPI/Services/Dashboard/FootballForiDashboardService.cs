using AngleSharp.Dom;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Infrastructure.Storage;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Services.Dashboard
{
    public class FootballForiDashboardService : IFootballForiDashboardService
    {
        private readonly IDebugStore _debugStore;
        private readonly INewsStorage _newsStorage;

        public FootballForiDashboardService(IDebugStore debugStore, INewsStorage newsStorage)
        {
            _debugStore = debugStore;
            _newsStorage = newsStorage;
        }

        public DebugSummary GetSummary()
        {
            var all = _debugStore.GetAllDebugItems();
            var channelQueueCounts = _newsStorage.GetChannelQueueCounts();


            var accepted = _debugStore.GetAcceptedItems().Count;
            var rejected = all.Count - accepted;
            var published = all.Count(x => x.PublishStatus == "ارسال شد");
            var inQueue = channelQueueCounts.Values.Sum();

            var acceptanceRate = all.Count > 0
                ? Math.Round((double)accepted / all.Count * 100, 1)
                : 0;

            var statusColor = inQueue > 50 ? "red" :
                              inQueue > 20 ? "yellow" :
                              acceptanceRate < 80 ? "yellow" : "green";

            var queueDetails = channelQueueCounts.Any(x => x.Value > 0)
                ? string.Join(" | ", channelQueueCounts
                    .Where(x => x.Value > 0)
                    .OrderByDescending(x => x.Value)
                    .Select(x => $"{CleanSourceName(x.Key)}: {x.Value}"))
                : "همه صف‌ها خالی!";


            var now = DateTimeOffset.Now;
            var recentAll = _debugStore.GetRecentProcessed(10000);

            // آخرین ساعت
            var lastHourItems = recentAll
                .Where(x => x.ProcessedAt > now.AddHours(-1))
                .ToList();

            // امروز
            var todayItems = recentAll
                .Where(x => x.ProcessedAt.Date == now.Date)
                .ToList();
            var lastHourAccepted = lastHourItems.Count(x => x.PublishStatus == "جدید_واکشی_شده" || x.PublishStatus == "در صف انتشار" || x.IsAccepted);
            var lastHourRejected = lastHourItems.Count - lastHourAccepted;
            // آمار آخرین ساعت — کاملاً واقعی و هماهنگ
            var lastHour = new
            {
                Total = lastHourItems.Count,
                Accepted = lastHourAccepted,
                Rejected = lastHourRejected,
                AcceptanceRate = lastHourItems.Count > 0
                    ? Math.Round((double)lastHourAccepted * 100 / lastHourItems.Count, 1)
                    : 0.0
            };
            var todayAccepted = todayItems.Count(x => x.PublishStatus == "جدید_واکشی_شده" || x.PublishStatus == "در صف انتشار" || x.IsAccepted);
            // آمار امروز — کامل و صادقانه
            var today = new
            {
                Total = todayItems.Count,
                Accepted = todayAccepted,
                Rejected = todayItems.Count - todayAccepted,
                AcceptanceRate = todayItems.Count > 0
                      ? Math.Round((double)todayAccepted * 100 / todayItems.Count, 1)
                      : 0.0
            };


            // رنگ داشبورد — بر اساس آخرین ساعت (واقعی‌تر)
            var dashboardStatusColor = lastHourAccepted >= lastHourItems.Count * 0.90 ? "green" :
                                     lastHourAccepted >= lastHourItems.Count * 0.70 ? "yellow" : "red";
             

            return new DebugSummary
            {
                TotalProcessed = all.Count,
                AcceptedCount = accepted,
                RejectedCount = rejected,
                PublishedCount = published,
                InQueueCount = inQueue,
                AcceptanceRate = acceptanceRate,
                SystemStatus = acceptanceRate >= 90 ? "عالی" : "خوب",
                StatusColor = statusColor,
                QueueDetails = queueDetails,

                LastHourItemsTotal= lastHour.Total,
                LastHourAccepted= lastHour.Accepted,
                LastHourRejected= lastHour.Rejected,
                LastHourAcceptanceRate= lastHour.AcceptanceRate,

                TodayItemsTotal= today.Total,
                TodayAccepted= today.Accepted,
                TodayRejected= today.Rejected,
                TodayAcceptanceRate= today.AcceptanceRate, 


                DashboardStatusColor= dashboardStatusColor
            };
        }
        private string CleanSourceName(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return "نامشخص";

            return source
                .Replace("؛ آخرین اخبار ورزشی ایران و جهان", "")
                .Replace("؛ آخرین اخبار ورزشی", "")
                .Replace("| آخرین اخبار ورزشی", "")
                .Replace(" - آخرین اخبار ورزشی", "")
                .Replace(" | Varzesh 3", "")
                .Replace(" - Varzesh3", "")
                .Replace(" - Khabarvarzeshi", "")
                .Replace("ورزش سه | آخرین اخبار ورزشی", "ورزش سه")
                .Replace("خبرورزشی؛ آخرین اخبار ورزشی ایران و جهان", "خبرورزشی")
                .Split(new[] { '|', '؛', '-' }, 2)[0]
                .Trim()
                .Replace("  ", " ");
        }



        public object GetFeed()
        {
            var queueBySource = _newsStorage.GetMirroQueue();
            return new
            {
                // صف‌ها به تفکیک منبع — همه خبرها رو نشون میده!
                queues = queueBySource.ToDictionary(
                     kvp => kvp.Key,
                     kvp => kvp.Value.Select((item, index) => new
                     {
                         score = item.Score,
                         position = index + 1,
                         title = item.Title?.Truncate(90),
                         link = item.Link,
                         pubDate = item.PubDate.ToString("HH:mm"),
                         source = item.SourceName ?? kvp.Key
                     })
                     .OrderByDescending(a => a.score)
                     .ToList()
                 ),

                totalInQueue = queueBySource.Sum(x => x.Value.Count),

                // آخرین خبرهای ارسال‌شده
                publishedItems = _debugStore.GetPublishedItems()
                     .Select((x, i) => new
                     {
                         score = x.Score,
                         position = i + 1,
                         source = x.SourceName ?? "نامشخص",
                         title = x.Title?.Truncate(80),
                         link = x.Link,
                         sentAt = x.PublishedAt.Value.ToString("HH:mm:ss") // چون PublishedAt نداریم، از ProcessedAt استفاده می‌کنیم
                     }).ToList(),

                // آخرین پردازش‌شده‌ها (قبول + رد + در صف)
                recentProcessed = _debugStore.GetRecentProcessed(80)
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
