using System.Collections.Concurrent;
using System.Threading.Channels;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public class NewsStorage
    {
        // تنظیمات
        private readonly int _maxItemsPerSource;
        private readonly int _publishQueueCapacity;

        // بافر هر منبع (برای فیلتر شباهت و تاریخ)
        private readonly ConcurrentDictionary<string, LinkedList<NewsItem>> _sourceBuffers = new();

        // کلیدهای دیده‌شده (جهانی — برای جلوگیری از تکرار کامل)
        private readonly ConcurrentDictionary<string, byte> _seenKeys = new();

        // کلیدهای ارسال‌شده (برای جلوگیری از ارسال دوباره)
        private readonly ConcurrentDictionary<string, byte> _processedKeys = new();

        // صف انتشار (Channel)
        public Channel<NewsItem> PublishChannel { get; }
        public ChannelReader<NewsItem> PublishReader => PublishChannel.Reader;
        public ChannelWriter<NewsItem> PublishWriter => PublishChannel.Writer;

        // آخرین منبع ارسال‌شده + قفل برای Thread-Safety
        private string? _lastSourcePublished;
        private readonly object _lastSourceLock = new();

        public string? LastSourcePublished
        {
            get => _lastSourcePublished;
            private set
            {
                lock (_lastSourceLock)
                    _lastSourcePublished = value;
            }
        }

        public NewsStorage(int maxItemsPerSource = 8, int publishQueueCapacity = 20)
        {
            _maxItemsPerSource = maxItemsPerSource;
            _publishQueueCapacity = publishQueueCapacity;

            var options = new BoundedChannelOptions(publishQueueCapacity)
            {
                SingleWriter = false,
                SingleReader = false,
                FullMode = BoundedChannelFullMode.Wait
            };

            PublishChannel = Channel.CreateBounded<NewsItem>(options);
        }

        public async ValueTask AddAsync(NewsItem item, CancellationToken ct = default)
        {
            if (item?.Link == null || item?.SourceName == null) return;

            var source = item.SourceName;
            var pubDate = item.PubDate;
            var globalKey = $"{item.Link}-{pubDate:yyyyMMddHHmm}";

            // 1. جلوگیری از تکرار کامل (جهانی)
            if (!_seenKeys.TryAdd(globalKey, 1)) return;

            // 2. دریافت یا ایجاد بافر منبع
            var buffer = _sourceBuffers.GetOrAdd(source, _ => new LinkedList<NewsItem>());

            // 3. چک شباهت با خبرهای اخیر همین منبع
            foreach (var old in buffer)
            {
                if (TextSimilarity.GetSimilarity(item.Title, old.Title) >= 0.92)
                    goto Rejected;

                var contentA = $"{item.Title} {item.ShortDescription} {item.FullDescription}".Trim();
                var contentB = $"{old.Title} {old.ShortDescription} {old.FullDescription}".Trim();
                if (!string.IsNullOrWhiteSpace(contentA) && !string.IsNullOrWhiteSpace(contentB))
                {
                    if (TextSimilarity.GetSimilarity(contentA, contentB) >= 0.90)
                        goto Rejected;
                }

                if (item.Link == old.Link)
                    goto Rejected;
            }

            // 4. فقط خبرهایی که خیلی قدیمی‌تر از آخرین خبر منبع نیستند
            if (buffer.Count > 0)
            {
                var newest = buffer.First!.Value;
                if (pubDate < newest.PubDate.AddHours(-12))
                    goto Rejected;
            }

            // قبول شد!
            buffer.AddFirst(item);

            // محدود کردن حافظه هر منبع
            while (buffer.Count > _maxItemsPerSource)
            {
                if (buffer.Last is { } oldest)
                {
                    var oldKey = $"{oldest.Value.Link}-{oldest.Value.PubDate:yyyyMMddHHmm}";
                    buffer.RemoveLast();
                    _seenKeys.TryRemove(oldKey, out _);
                }
            }

            // تمیزکاری بافر خالی
            if (buffer.Count == 0)
                _sourceBuffers.TryRemove(source, out _);

            // اضافه به صف انتشار
            if (!PublishWriter.TryWrite(item))
                await PublishWriter.WriteAsync(item, ct);

            return;

        Rejected:
            _seenKeys.TryRemove(globalKey, out _);
        }

        public void UpdateLastPublished(string source, DateTimeOffset time)
        {
            LastSourcePublished = source;
        }

        public bool HasOtherSourceSafe(string currentSource)
        {
            // اگه تو صف فقط از همین منبع باشه → اجازه بده بفرسته
            if (PublishReader.Count == 0) return false;

            // اگه فقط ۱ خبر تو صف باشه → چه از همین منبع چه نه → بذار بره
            if (PublishReader.Count == 1) return false;

            // نگاه کن ببین تو صف، خبر از منبع دیگه هست یا نه
            var hasOther = false;
            var tempList = new List<NewsItem>();

            while (PublishReader.TryRead(out var next))
            {
                if (next != null && next.SourceName != currentSource)
                {
                    hasOther = true;
                }
                tempList.Add(next!);
            }

            // همه رو دوباره برگردون به صف
            foreach (var item in tempList)
                PublishWriter.TryWrite(item);

            return hasOther;
        }

        public async ValueTask RepublishAsync(NewsItem item, CancellationToken ct = default)
        {
            if (!PublishWriter.TryWrite(item))
                await PublishWriter.WriteAsync(item, ct);
        }

        public bool IsAlreadyProcessed(string key) => _processedKeys.ContainsKey(key);
        public void MarkAsProcessed(string key) => _processedKeys[key] = 1;

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

        public void Complete()
        {
            PublishWriter.Complete();
        }
    }
}