using System.Collections.Concurrent;
using System.Threading.Channels;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public class NewsStorage
    {
        // تنظیمات طلایی و ابدی برای سرور ۱۲۸ مگابایتی تو
        private const int MaxSources = 50;        // حداکثر منبع همزمان
        private const int MaxSeenKeys = 100;      // حداکثر کلید دیده‌شده (لینک + دقیقه)
        private const int DefaultQueueCapacity = 10;

        // آخرین خبر هر منبع
        private readonly ConcurrentDictionary<string, NewsItem> _lastSentBySource = new();

        // ترتیب اضافه شدن منابع (برای حذف قدیمی‌ترین در صورت نیاز)
        private static readonly ConcurrentQueue<string> _sourceOrder = new();

        // کلیدهای دیده‌شده + ترتیب (LRU)
        private static readonly ConcurrentDictionary<string, byte> _seenKeys = new();
        private static readonly ConcurrentQueue<string> _seenKeysOrder = new();

        // صف انتشار
        public Channel<NewsItem> PublishChannel { get; }
        public ChannelReader<NewsItem> PublishReader => PublishChannel.Reader;
        public ChannelWriter<NewsItem> PublishWriter => PublishChannel.Writer;

        public NewsStorage(int publishQueueCapacity = DefaultQueueCapacity)
        {
            var options = new BoundedChannelOptions(publishQueueCapacity)
            {
                SingleWriter = false,
                SingleReader = false,
                FullMode = BoundedChannelFullMode.DropOldest,
            };
            PublishChannel = Channel.CreateBounded<NewsItem>(options);
        }

        public async ValueTask AddAsync(NewsItem item, CancellationToken ct = default)
        {
            if (item?.SourceName is not { } source ||
                item.Link is null ||
                item.PubDate == default)
                return;

            var pubDate = item.PubDate;

            // لایه ۱: خبر دقیقاً یکسان (لینک + دقیقه)
            var globalKey = $"{item.Link}-{pubDate:yyyyMMddHHmm}";
            if (!_seenKeys.TryAdd(globalKey, 1))
                return;

            _seenKeysOrder.Enqueue(globalKey);
            if (_seenKeys.Count > MaxSeenKeys)
            {
                if (_seenKeysOrder.TryDequeue(out var oldestKey))
                    _seenKeys.TryRemove(oldestKey, out _);
            }

            // لایه ۲: جدیدتر از آخرین خبر همین منبع
            if (_lastSentBySource.TryGetValue(source, out var lastFromSource))
            {
                if (item.PubDate <= lastFromSource.PubDate)
                    return;
            }

            // لایه ۳: چک شباهت با آخرین خبر منابع دیگر
            foreach (var kvp in _lastSentBySource)
            {
                if (kvp.Key == source) continue;

                var other = kvp.Value;

                if (TextSimilarity.GetSimilarity(item.Title, other.Title) >= 0.92)
                    return;

                var a = $"{item.Title} {item.ShortDescription} {item.FullDescription}".Trim();
                var b = $"{other.Title} {other.ShortDescription} {other.FullDescription}".Trim();

                if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b))
                {
                    if (TextSimilarity.GetSimilarity(a, b) >= 0.90)
                        return;
                }
            }

            // خبر قبول شد
            if (!PublishWriter.TryWrite(item))
                await PublishWriter.WriteAsync(item, ct);
         
            _lastSentBySource[source] = item;
 

          
        }
         

        public IReadOnlyCollection<NewsItem> GetCurrentPublishQueue()
        {
            var list = new List<NewsItem>();
            while (PublishReader.TryRead(out var item))
                list.Add(item);
            // دوباره برگردون به صف
            foreach (var i in list)
                PublishWriter.TryWrite(i);
            return list;
        }
 
    }
}