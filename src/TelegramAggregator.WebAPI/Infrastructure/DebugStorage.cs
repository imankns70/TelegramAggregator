using System.Collections.Concurrent;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public class DebugStorage
    {
        private readonly ConcurrentQueue<DebugFeedItem> _items = new();
        private const int MaxItems = 300;

        private readonly NewsStorage _newsStorage;
        private readonly object _queueLock = new();

        public DebugStorage(NewsStorage newsStorage)
        {
            _newsStorage = newsStorage;
        }

        public void Add(DebugFeedItem item)
        {
            _items.Enqueue(item);

            while (_items.Count > MaxItems)
                _items.TryDequeue(out _);
        }

        // همه آیتم‌ها (قبول/رد + ارسال‌شده + در صف)
        public IReadOnlyList<DebugFeedItem> GetAllItems()
        {
            return _items.Reverse().ToList();
        }

        // فقط خبرهای ارسال‌شده
        public IReadOnlyList<DebugFeedItem> GetPublishedItems()
        {
            return _items
                .Where(x => x.PublishStatus == "ارسال شد")
                .Reverse()
                .Take(50)
                .ToList();
        }

        // صف فعلی انتشار — زنده و دقیق
        public IReadOnlyList<DebugFeedItem> GetCurrentPublishQueue()
        {
            var queueItems = new List<DebugFeedItem>();
            var tempItems = new List<NewsItem>();

            lock (_queueLock)
            {
                while (_newsStorage.PublishReader.TryRead(out var item))
                {
                    if (item != null)
                    {
                        tempItems.Add(item);
                        queueItems.Add(new DebugFeedItem
                        {
                            SourceName = item.SourceName,
                            Title = item.Title,
                            Link = item.Link,
                            PubDate = item.PubDate,
                            IsAccepted = true,
                            RejectReason = null,
                            PublishStatus = "در صف انتشار",
                            PublishedAt = null,
                            ProcessedAt = DateTimeOffset.Now
                        });
                    }
                }

                // همه رو دوباره برگردون به صف
                foreach (var item in tempItems)
                    _newsStorage.PublishWriter.TryWrite(item);
            }

            return queueItems.OrderBy(x => x.PubDate).ToList();
        }

        // خلاصه سریع برای داشبورد
        public DebugSummary GetSummary()
        {
            var all = _items.ToArray();
            return new DebugSummary
            {
                TotalProcessed = all.Length,
                AcceptedCount = all.Count(x => x.IsAccepted),
                RejectedCount = all.Count(x => !x.IsAccepted),
                PublishedCount = all.Count(x => x.PublishStatus == "ارسال شد"),
                InQueueCount = GetCurrentPublishQueue().Count
            };
        }
    }

 
}