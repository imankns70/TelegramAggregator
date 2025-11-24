using System.Collections.Concurrent;
using System.Threading.Channels;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public class NewsStorage
    {
        private readonly int _maxItem;
        // فیلتر مرکزی — مشترک برای همه منابع
        private static readonly ConcurrentDictionary<string, byte> _seenKeys = new();
        private static readonly ConcurrentQueue<string> _seenKeysOrder = new();

        private static readonly ConcurrentDictionary<string, NewsItem> _lastSentBySource = new();
        private readonly ConcurrentQueue<string> _lastSentOrder = new(); // ترتیب اضافه شدن
        private readonly object _lastSentLock = new(); // برای thread-safety

        // کانال‌های جداگانه — هر منبع مال خودش
        internal readonly ConcurrentDictionary<string, Channel<NewsItem>> _sourceChannels = new();
        private readonly BoundedChannelOptions _channelOptions;
        // اینا اضافه شدن — قلب جدید دیباگ!
        private readonly ConcurrentDictionary<string, DebugFeedItem> _debugItems = new();
        private const int MaxDebugItems = 600;

        // آینه برای دیباگ
        private readonly ConcurrentDictionary<string, ConcurrentQueue<NewsItem>> _mirrorQueues = new();

        public NewsStorage(int queueCapacityPerSource, int maxSeenKeys)
        {
            _maxItem = maxSeenKeys;
            _channelOptions = new BoundedChannelOptions(queueCapacityPerSource)
            {
                SingleWriter = true,
                SingleReader = false,
                FullMode = BoundedChannelFullMode.DropOldest
            };

        }

        public bool TryDequeueAny(out NewsItem? item)
        {
            item = null;
            var channels = _sourceChannels.Values.Where(ch => ch.Reader.Count > 0).ToList();
            if (!channels.Any()) return false;

            var selected = channels[Random.Shared.Next(channels.Count)];

            // dequeue از کانال اصلی
            if (!selected.Reader.TryRead(out item))
                return false;

            // حذف از آینه
            if (item != null &&
                _mirrorQueues.TryGetValue(item.SourceName, out var mirror))
            {
                mirror.TryDequeue(out _);
            }

            return true;
        }

        public async ValueTask AddAsync(NewsItem item, CancellationToken ct = default)
        {
            if (item?.SourceName is not { } source ||
                item.Link is null ||
                item.PubDate == default)
                return;

            var pubDate = item.PubDate;

            // لایه ۱: کلید جهانی (لینک + دقیقه)
            var globalKey = $"{item.Link}-{pubDate:yyyyMMddHHmm}";
            if (!_seenKeys.TryAdd(globalKey, 1))
            {
                LogRejected(item, "تکراری (کلید جهانی)");
                return;
            }


            _seenKeysOrder.Enqueue(globalKey);
            if (_seenKeys.Count > _maxItem)
                if (_seenKeysOrder.TryDequeue(out var oldestKey))
                    _seenKeys.TryRemove(oldestKey, out _);

            // لایه ۲: فقط خبرهای قدیمی‌تر از آخرین خبر این منبع رو رد کن
            if (_lastSentBySource.TryGetValue(source, out var lastFromSource))
            {
                if (item.PubDate <= lastFromSource.PubDate)
                {
                    LogRejected(item, "قدیمی‌تر از آخرین ارسال‌شده");
                    //"تکراری (قدیمی‌تر یا هم‌زمان
                    return;
                }
            }

            // لایه ۳: شباهت با آخرین خبرهای منابع دیگر
            foreach (var kvp in _lastSentBySource.Where(x => x.Key != source))
            {
                var other = kvp.Value;
                if (TextSimilarity.GetSimilarity(item.Title, other.Title) >= 0.92) return;
                var a = $"{item.Title} {item.ShortDescription} {item.FullDescription}".Trim();
                var b = $"{other.Title} {other.ShortDescription} {other.FullDescription}".Trim();
                if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b))
                    if (TextSimilarity.GetSimilarity(a, b) >= 0.90)
                    {
                        LogRejected(item, $"شبیه به خبر اخیر {other.UniqueKey}");
                        return;
                    }
            }


            // قبول شد!
            LogAccepted(item);

            // خبر قبول شد — برو تو صف منبع خودت
            var channel = _sourceChannels.GetOrAdd(source, _ => Channel.CreateBounded<NewsItem>(_channelOptions));
            if (!channel.Writer.TryWrite(item))
                await channel.Writer.WriteAsync(item, ct);


            // آینه
            var mirror = _mirrorQueues.GetOrAdd(source, _ => new ConcurrentQueue<NewsItem>());
            mirror.Enqueue(item);


            // فقط الان! آخرین خبر این منبع رو آپدیت کن + مدیریت حافظه
            lock (_lastSentLock)
            {
                // اگه اولین باره، به ترتیب اضافه کن
                if (!_lastSentBySource.ContainsKey(source))
                {
                    _lastSentOrder.Enqueue(source);
                }

                // آپدیت کن
                _lastSentBySource[source] = item;


                while (_lastSentOrder.Count > _maxItem)
                {
                    if (_lastSentOrder.TryDequeue(out var oldestSource))
                    {
                        _lastSentBySource.TryRemove(oldestSource, out _);
                    }
                }
            }

        }

        private void LogOrUpdate(DebugFeedItem item)
        {
            var key = item.UniqueKey;
            _debugItems.AddOrUpdate(
                key,
                item,
                (k, existing) =>
                {
                    // فقط وضعیت جدیدتر رو نگه دار (بر اساس زمان)
                    if (item.ProcessedAt > existing.ProcessedAt)
                        return item;
                    return existing;
                });

            // محدودیت تعداد
            while (_debugItems.Count > MaxDebugItems)
            {
                var oldest = _debugItems.OrderBy(x => x.Value.ProcessedAt).FirstOrDefault();
                if (oldest.Key != null)
                    _debugItems.TryRemove(oldest.Key, out _);
            }
        }

        // همه متدهای دیباگ — الان اینجا هستن
        public IReadOnlyList<DebugFeedItem> GetAllDebugItems() => _debugItems.Values.ToArray();

        public IReadOnlyList<DebugFeedItem> GetPublishedItems() =>
            _debugItems.Where(x => x.Value.PublishStatus == "ارسال شد")
                       .Reverse()
                       .Take(60)
                       .Select(a => a.Value)
                       .ToList();

        public IReadOnlyList<DebugFeedItem> GetRecentProcessed(int take = 40) =>
            _debugItems.Values.Reverse().Take(take).ToList();

        public IReadOnlyDictionary<string, int> GetQueueCounts() =>
            _sourceChannels.ToDictionary(x => x.Key, x => x.Value.Reader.Count);

        // صف‌ها — همه خبرها رو نشون بده (ایمن با TryPeek)
        public IReadOnlyDictionary<string, List<DebugFeedItem>> GetQueueBySource()
        {
            var result = new Dictionary<string, List<DebugFeedItem>>();

            foreach (var kvp in _mirrorQueues)
            {
                var source = kvp.Key;
                var snapshot = kvp.Value.ToList(); // امن + بدون قفل

                var items = snapshot.Select(item => new DebugFeedItem
                {
                    SourceName = source,
                    Title = item.Title,
                    Link = item.Link,
                    PubDate = item.PubDate,
                    PublishStatus = "در صف"
                }).ToList();

                result[source] = items;
            }

            return result;
        }

        public DebugSummary GetSummary()
        {
            var all = _debugItems.ToArray();
            var queueCounts = GetQueueCounts();

            var accepted = all.Count(x => x.Value.IsAccepted);
            var rejected = all.Length - accepted;
            var published = all.Count(x => x.Value.PublishStatus == "ارسال شد");
            var inQueue = queueCounts.Values.Sum();

            var acceptanceRate = accepted + rejected > 0
                ? Math.Round((double)accepted / (accepted + rejected) * 100, 1)
                : 0;

            var statusColor = inQueue > 50 ? "red" : inQueue > 20 ? "yellow" : "green";
            var queueDetails = queueCounts.Any(x => x.Value > 0)
                ? string.Join(" | ", queueCounts.Where(x => x.Value > 0)
                    .OrderByDescending(x => x.Value)
                    .Select(x => $"{x.Key}: {x.Value}"))
                : "همه صف‌ها خالی";

            return new DebugSummary
            {
                TotalProcessed = all.Length,
                AcceptedCount = accepted,
                RejectedCount = rejected,
                PublishedCount = published,
                InQueueCount = inQueue,
                AcceptanceRate = acceptanceRate,
                StatusColor = statusColor,
                QueueDetails = queueDetails
            };
        }

        // وقتی خبر رد شد
        public void LogRejected(NewsItem item, string reason)
        {
            LogOrUpdate(new DebugFeedItem
            {
                SourceName = item.SourceName!,
                Title = item.Title,
                Link = item.Link,
                PubDate = item.PubDate,
                IsAccepted = false,
                RejectReason = reason,
                PublishStatus = "رد شد",
                ProcessedAt = DateTimeOffset.Now
            });
        }

        // وقتی خبر قبول شد
        public void LogAccepted(NewsItem item)
        {
            LogOrUpdate(new DebugFeedItem
            {
                SourceName = item.SourceName!,
                Title = item.Title,
                Link = item.Link,
                PubDate = item.PubDate,
                IsAccepted = true,
                PublishStatus = "در صف انتشار",
                ProcessedAt = DateTimeOffset.Now
            });
        }

        public void LogPublished(NewsItem item)
        {
            LogOrUpdate(new DebugFeedItem
            {
                SourceName = item.SourceName!,
                Title = item.Title,
                Link = item.Link,
                PubDate = item.PubDate,
                IsAccepted = true,
                PublishStatus = "ارسال شد",
                ProcessedAt = DateTimeOffset.Now
            });
        }

    }
}