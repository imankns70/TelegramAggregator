using System.Collections.Concurrent;
using System.Linq;
using System.Runtime;
using System.Threading.Channels;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Infrastructure.Storage;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public class NewsStorage : INewsStorage
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
        public void ClearAllQueues()
        {
            foreach (var kvp in _sourceChannels)
            {
                var channel = kvp.Value;
                while (channel.Reader.TryRead(out _)) { }
            }

            _debugMirror.Clear();


        }

        public bool TryDequeueAny(out NewsItem item)
        {
            item = null;
            var bestCandidates = new List<(NewsItem Item, string Source, DateTimeOffset PubDate)>();
            double maxScore = double.MinValue;

            // مرحله ۱: اسکن همه صف‌ها و پیدا کردن بهترین‌ها
            foreach (var kvp in _sourceChannels)
            {
                var source = kvp.Key;
                var channel = kvp.Value;
                var temp = new List<NewsItem>();

                while (channel.Reader.TryRead(out var n))
                {
                    temp.Add(n);
                     
                    if (n.Score > maxScore)
                    {
                        maxScore = n.Score;
                        bestCandidates.Clear();
                        bestCandidates.Add((n, source, n.PubDate));
                    }
                    else if (n.Score == maxScore)
                    {
                        bestCandidates.Add((n, source, n.PubDate));
                    }
                }

                // همه رو برگردون به صف
                foreach (var n in temp)
                    channel.Writer.TryWrite(n);
            }

            if (bestCandidates.Count == 0)
                return false;

            // مرحله ۲: از بین بهترین‌ها → قدیمی‌ترین رو انتخاب کن
            var selected = bestCandidates
                .OrderBy(x => x.PubDate)  // قدیمی‌ترین اول
                .First();

            item = selected.Item;
            var selectedSource = selected.Source;

            // مرحله ۳: حذف واقعی از صف اصلی
            if (_sourceChannels.TryGetValue(selectedSource, out var ch))
            {
                var temp = new List<NewsItem>();
                while (ch.Reader.TryRead(out var n))
                {
                    if (n.UniqueKey != item.UniqueKey)
                        temp.Add(n);
                }
                foreach (var n in temp)
                    ch.Writer.TryWrite(n);
            }

            // مرحله ۴: حذف از میرور
            if (_debugMirror.TryGetValue(selectedSource, out var m))
            {
                var temp = new List<NewsItem>();
                while (m.TryDequeue(out var x))
                {
                    if (x.UniqueKey != item.UniqueKey)
                        temp.Add(x);
                }
                foreach (var x in temp)
                    m.Enqueue(x);
            }

            return true;
        }
        // کلید یکتا فقط بر اساس لینک — برای همه منابع یکسان و بدون خطا
        private static string GenerateGlobalKey(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
                return Guid.NewGuid().ToString(); // fallback

            // فقط قسمت اصلی لینک رو نگه دار (بدون پارامتر و hash)
            var uri = new Uri(link);
            var cleanPath = uri.AbsolutePath;

            // حذف اسلش آخر اگر بود
            if (cleanPath.EndsWith("/"))
                cleanPath = cleanPath.TrimEnd('/');

            // ترکیب هاست + مسیر تمیز = کلید ۱۰۰٪ یکتا
            return $"{uri.Host}{cleanPath}".ToLowerInvariant();
        }

        // استفاده در کد:

        public async Task<(bool, string)> TryAddAsync(NewsItem item, CancellationToken ct = default)
        {

            string source = item?.SourceName;
            var pubDate = item.PubDate;

            // لایه ۱: کلید جهانی (لینک + دقیقه)
            //var globalKey = $"{item.Link}-{pubDate:yyyyMMddHHmm}";
            var globalKey = GenerateGlobalKey(item.Link);
            if (!_seenKeys.TryAdd(globalKey, 1))
            {

                return (false, "تکراری فیلتر لایه اول)");
            }

            _seenKeysOrder.Enqueue(globalKey);
            if (_seenKeys.Count > _maxItem)
                if (_seenKeysOrder.TryDequeue(out var oldestKey))
                    _seenKeys.TryRemove(oldestKey, out _);


            // لایه ۳: شباهت با آخرین خبرهای منابع دیگر
            foreach (var kvp in _lastSentBySource.Where(x => x.Key != source))
            {
                var other = kvp.Value;
                if (TextSimilarity.GetSimilarity(item.Title, other.Title) >= 0.92)
                {
                    return (false, $"شبیه به خبر اخیر لایه سوم {other.UniqueKey}");
                }
                ;
                var a = $"{item.Title} {item.ShortDescription} {item.FullDescription}".Trim();
                var b = $"{other.Title} {other.ShortDescription} {other.FullDescription}".Trim();
                if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b))
                    if (TextSimilarity.GetSimilarity(a, b) >= 0.90)
                    {

                        return (false, $"شبیه به خبر اخیر لایه سوم {other.UniqueKey}");
                    }
            }



            var channel = _sourceChannels.GetOrAdd(source, _ => Channel.CreateBounded<NewsItem>(_channelOptions));
            var mirrorQueue = _debugMirror.GetOrAdd(source, _ => new ConcurrentQueue<NewsItem>());

            // تلاش برای نوشتن در کانال
            bool wasFull = !channel.Writer.TryWrite(item);

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
            return (true, "");

        }

        public IReadOnlyDictionary<string, int> GetChannelQueueCounts() =>
            _sourceChannels.ToDictionary(x => x.Key, x => x.Value.Reader.Count);


        public IReadOnlyDictionary<string, List<DebugFeedItem>> GetMirroQueue()
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



        public int GetSeenKeys() => _seenKeys.Count;


    }
}