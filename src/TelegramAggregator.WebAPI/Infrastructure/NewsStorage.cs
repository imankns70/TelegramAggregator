using System.Collections.Concurrent;
using System.Linq;
using System.Runtime;
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
        internal readonly ConcurrentDictionary<string, DebugFeedItem> _debugItems = new();
        private const int MaxDebugItems = 400;

        private readonly ConcurrentDictionary<string, ConcurrentQueue<NewsItem>> _debugMirror = new();


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
        public bool TryDequeueAny2(out NewsItem? item)
        {
            item = null;

            // Shuffle channels to avoid bias
            var channels = _sourceChannels.Values.ToList();
            if (channels.Count == 0)
                return false;

            // انتخاب تصادفی یک کانال تا زمانی که آیتم داشته باشد
            for (int i = 0; i < channels.Count; i++)
            {
                var idx = Random.Shared.Next(channels.Count);
                var channel = channels[idx];

                if (!channel.Reader.TryRead(out var readItem))
                    continue;

                item = readItem;

                // MIRROR SYNCHRONIZATION
                if (_debugMirror.TryGetValue(item.SourceName, out var mirrorQueue))
                {
                    mirrorQueue.TryDequeue(out _); // drop oldest to reflect real read
                }

                return true;
            }

            return false;
        }
        public bool TryDequeueAny(out NewsItem? item)
        {
            item = null;
            NewsItem? best = null;
            string? bestSource = null;
            double max = double.MinValue;

            foreach (var kvp in _sourceChannels)
            {
                var source = kvp.Key;
                var channel = kvp.Value;
                var temp = new List<NewsItem>();

                while (channel.Reader.TryRead(out var n))
                {
                    temp.Add(n);
                    if (n.Score > max)
                    {
                        max = n.Score;
                        best = n;
                        bestSource = source;
                    }
                }

                foreach (var n in temp)
                    channel.Writer.TryWrite(n);
            }

            if (best == null) return false;

            item = best;

            // حذف از صف اصلی
            if (_sourceChannels.TryGetValue(bestSource!, out var ch))
            {
                var temp = new List<NewsItem>();
                while (ch.Reader.TryRead(out var n))
                    if (n.UniqueKey != item.UniqueKey)
                        temp.Add(n);
                foreach (var n in temp)
                    ch.Writer.TryWrite(n);
            }

            // حذف از آینه
            if (_debugMirror.TryGetValue(bestSource!, out var m))
            {
                var temp = new List<NewsItem>();
                while (m.TryDequeue(out var x))
                    if (x.UniqueKey != item.UniqueKey)
                        temp.Add(x);
                foreach (var x in temp)
                    m.Enqueue(x);
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
                LogRejected(item, "تکراری فیلتر لایه اول)");
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
                    LogRejected(item, "قدیمی، فیلتر لایه دوم");
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
                        LogRejected(item, $"شبیه به خبر اخیر لایه سوم {other.UniqueKey}");
                        return;
                    }
            }


            // قبول شد!
            LogAccepted(item);

            var channel = _sourceChannels.GetOrAdd(source, _ => Channel.CreateBounded<NewsItem>(_channelOptions));
            var mirrorQueue = _debugMirror.GetOrAdd(source, _ => new ConcurrentQueue<NewsItem>());

            // تلاش برای نوشتن در کانال
            bool wasFull = !channel.Writer.TryWrite(item);

            // اگر پر بود → کانال DropOldest انجام می‌دهد
            // و WriteAsync جایگزین می‌کند
            if (wasFull)
            {
                await channel.Writer.WriteAsync(item, ct);
                // آینه هم باید DropOldest انجام دهد
                mirrorQueue.TryDequeue(out _);
            }

            // حالا آیتم جدید را در آینه اضافه می‌کنیم
            mirrorQueue.Enqueue(item);

            // بیمهٔ ظرفیت — هرگز بیشتر از Capacity نشود
            while (mirrorQueue.Count > _channelOptions.Capacity)
                mirrorQueue.TryDequeue(out _);


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
            while (_debugItems.Count > MaxDebugItems)
            {
                var oldest = _debugItems.OrderBy(x => x.Value.ProcessedAt).First();
                _debugItems.TryRemove(oldest.Key, out _);
            }



        }

        // همه متدهای دیباگ — الان اینجا هستن
        public IReadOnlyList<DebugFeedItem> GetAllDebugItems() => _debugItems.Values.ToArray();

        public IReadOnlyList<DebugFeedItem> GetPublishedItems()
        {
            var res= _debugItems.Where(x => x.Value.PublishStatus == "ارسال شد")
                                  .OrderByDescending(x => x.Value.PublishedAt)
                                  .Take(60)
                                  .Select(a => a.Value)
                                  .ToList();
            return res;
        }


        public IReadOnlyList<DebugFeedItem> GetRecentProcessed(int take = 40) =>
            _debugItems.Values.OrderByDescending(a => a.Score).Take(take).ToList();

        public IReadOnlyDictionary<string, int> GetQueueCounts() =>
            _sourceChannels.ToDictionary(x => x.Key, x => x.Value.Reader.Count);


        public IReadOnlyDictionary<string, List<DebugFeedItem>> GetQueueBySource()
        {
            return _debugMirror
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value
                        .Select(item => new DebugFeedItem
                        {
                            Score = item.Score,
                            SourceName = kvp.Key,
                            Title = item.Title ?? "بدون عنوان",
                            Link = item.Link,
                            PubDate = item.PubDate,
                            PublishStatus = "در صف انتشار",
                            ProcessedAt = DateTimeOffset.Now

                        })
                        .ToList())
                .Where(x => x.Value.Any())
                .ToDictionary(x => x.Key, x => x.Value);
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

        public DebugSummary GetSummary()
        {
            var all = _debugItems.ToArray();
            var queueCounts = GetQueueCounts();

            bool IsActuallyAccepted(KeyValuePair<string, DebugFeedItem> kv)
            {
                var x = kv.Value;
                return x.PublishStatus is "جدید_واکشی_شده" or "در صف انتشار" || x.IsAccepted;
            }

            var accepted = all.Count(IsActuallyAccepted);
            var rejected = all.Length - accepted;
            var published = all.Count(x => x.Value.PublishStatus == "ارسال شد");
            var inQueue = queueCounts.Values.Sum();

            var acceptanceRate = all.Length > 0
                ? Math.Round((double)accepted / all.Length * 100, 1)
                : 0;

            var statusColor = inQueue > 50 ? "red" :
                              inQueue > 20 ? "yellow" :
                              acceptanceRate < 80 ? "yellow" : "green";

            var queueDetails = queueCounts.Any(x => x.Value > 0)
                ? string.Join(" | ", queueCounts
                    .Where(x => x.Value > 0)
                    .OrderByDescending(x => x.Value)
                    .Select(x => $"{CleanSourceName(x.Key)}: {x.Value}"))
                : "همه صف‌ها خالی!";

            return new DebugSummary
            {
                TotalProcessed = all.Length,
                AcceptedCount = accepted,
                RejectedCount = rejected,
                PublishedCount = published,
                InQueueCount = inQueue,
                AcceptanceRate = acceptanceRate,
                SystemStatus = acceptanceRate >= 90 ? "عالی" : "خوب",
                StatusColor = statusColor,
                QueueDetails = queueDetails
            };
        }
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