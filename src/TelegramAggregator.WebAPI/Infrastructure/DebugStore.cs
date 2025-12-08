using System.Collections.Concurrent;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public class DebugStore : IDebugStore
    {
        private ConcurrentDictionary<string, DebugFeedItem> Items { get; } = new();
        private const int MaxDebugItems = 200;
       
        public void LogOrUpdate(DebugFeedItem item)
        {

            var key = item.UniqueKey;

            Items.AddOrUpdate(
                key,
                item, // اولین ثبت
                (k, existing) =>
                {
                    // اگر آیتم جدیدتر است، اطلاعات تازه را اعمال کن
                    if (item.ProcessedAt > existing.ProcessedAt)
                    {
                        existing.IsAccepted = item.IsAccepted;
                        existing.RejectReason = item.RejectReason;
                        existing.PublishStatus = item.PublishStatus;
                        existing.PublishedAt = item.PublishedAt;
                        existing.ProcessedAt = item.ProcessedAt;
                        existing.Title = item.Title ?? existing.Title; // در صورت تغییر
                    }

                    return existing;
                });

            // پاک‌سازی قدیمی‌ها
            while (Items.Count > MaxDebugItems)
            {
                var oldest = Items.OrderBy(x => x.Value.ProcessedAt).First();
                Items.TryRemove(oldest.Key, out _);
            }

        }
       
        // همه متدهای دیباگ — الان اینجا هستن
        public IReadOnlyList<DebugFeedItem> GetAllDebugItems() => Items.Values.ToArray();

        public IReadOnlyList<DebugFeedItem> GetPublishedItems()
        {
            var res = Items.Where(x => x.Value.PublishStatus == "ارسال شد")
                                  .OrderByDescending(x => x.Value.PublishedAt)
                                  .Take(60)
                                  .Select(a => a.Value)
                                  .ToList();
            return res;
        }

        public IReadOnlyList<DebugFeedItem> GetAcceptedItems()
        {
            var res = Items.Where(x => x.Value.PublishStatus == "جدید_واکشی_شده" || x.Value.PublishStatus == "در صف انتشار" || x.Value.IsAccepted)
                                  
                                  .Take(60)
                                  .Select(a => a.Value)
                                  .ToList();
            return res;
        }


        public IReadOnlyList<DebugFeedItem> GetRecentProcessed(int take = 40) =>
            Items.Values.OrderByDescending(a => a.Score).Take(take).ToList();

        // وقتی خبر رد شد
        public void LogRejected(NewsItem item, string reason)
        {
            LogOrUpdate(new DebugFeedItem
            {
                Score = item.Score,
                SourceName = item.SourceName!,
                Title = item.Title,
                Link = item.Link,
                PubDate = item.PubDate,
                IsAccepted = false,
                RejectReason = reason,
                PublishStatus = "رد شد",
                ProcessedAt = DateTimeOffset.Now,
                PublishedAt = DateTimeOffset.Now
            });
        }

        // وقتی خبر قبول شد
        public void LogAccepted(NewsItem item)
        {
            LogOrUpdate(new DebugFeedItem
            {
                Score = item.Score,
                SourceName = item.SourceName!,
                Title = item.Title,
                Link = item.Link,
                PubDate = item.PubDate,
                IsAccepted = true,
                PublishStatus = "در صف انتشار",
                ProcessedAt = DateTimeOffset.Now,
                PublishedAt = DateTimeOffset.Now
            });
        }

        public void LogPublished(NewsItem item)
        {
            LogOrUpdate(new DebugFeedItem
            {
                Score = item.Score,
                SourceName = item.SourceName!,
                Title = item.Title,
                Link = item.Link,
                PubDate = item.PubDate,
                IsAccepted = true,
                PublishStatus = "ارسال شد",
                ProcessedAt = DateTimeOffset.Now,
                PublishedAt = DateTimeOffset.Now
            });
        }

    }
}
