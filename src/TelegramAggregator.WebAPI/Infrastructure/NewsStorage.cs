using System.Collections.Concurrent;
using System.Threading.Channels;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure;

/// <summary>
/// ذخیره‌سازی هوشمند خبرها با چک منبع‌محور، شباهت، تاریخ و پشتیبانی از عملیات موازی
/// </summary>
public sealed class NewsStorage
{
    private readonly Channel<NewsItem> _publishChannel;

    // بافر جداگانه برای هر منبع — Thread-Safe
    private readonly ConcurrentDictionary<string, LinkedList<NewsItem>> _sourceBuffers = new();

    // کلید سراسری برای جلوگیری از تکرار کامل
    private readonly ConcurrentDictionary<string, byte> _seenKeys = new();

    private const int PublishQueueCapacity = 10;
    private const double SimilarityThreshold = 0.85;
    private readonly int _maxItemsPerSource;

    public string? LastSourcePublished { get; private set; }
    public DateTimeOffset? LastPublishedAt { get; private set; }
    public ChannelReader<NewsItem> PublishReader => _publishChannel.Reader;

    public NewsStorage(int maxItemsPerSource)
    {
        _maxItemsPerSource = maxItemsPerSource > 0 ? maxItemsPerSource : 100;

        _publishChannel = Channel.CreateBounded<NewsItem>(
            new BoundedChannelOptions(PublishQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true
            });
    }

    /// <summary>
    /// اضافه کردن خبر با چک منبع‌محور (موازی با انتشار)
    /// </summary>
    public async ValueTask AddAsync(NewsItem item, CancellationToken ct = default)
    {
        if (item?.Link is null || item?.SourceName is null) return;

        var source = item.SourceName;
        var key = $"{item.Link}-{item.PubDate:yyyyMMddHHmm}";

        // 1. چک کلید سراسری (Thread-Safe)
        if (_seenKeys.ContainsKey(key))
            return;

        // 2. دریافت یا ایجاد بافر منبع
        var buffer = _sourceBuffers.GetOrAdd(source, _ => new LinkedList<NewsItem>());

        // 3. چک شباهت با خبرهای اخیر همین منبع
        foreach (var old in buffer)
        {
            if (TextSimilarity.GetSimilarity(item.Title, old.Title) >= SimilarityThreshold)
                return;

            var contentA = $"{item.Title} {item.ShortDescription} {item.FullDescription}".Trim();
            var contentB = $"{old.Title} {old.ShortDescription} {old.FullDescription}".Trim();
            if (!string.IsNullOrWhiteSpace(contentA) && !string.IsNullOrWhiteSpace(contentB))
            {
                if (TextSimilarity.GetSimilarity(contentA, contentB) >= SimilarityThreshold)
                    return;
            }

            if (!string.IsNullOrWhiteSpace(item.Link) && item.Link == old.Link)
                return;
        }

        // 4. چک تاریخ: فقط جدیدتر از آخرین خبر همین منبع
        if (buffer.Count > 0)
        {
            var lastItemInSource = buffer.First!.Value;
            if (item.PubDate <= lastItemInSource.PubDate)
                return;
        }

        // 5. اضافه به ابتدای بافر منبع
        buffer.AddFirst(item);
        _seenKeys[key] = 1;

        // 6. محدود کردن حافظه هر منبع
        if (buffer.Count > _maxItemsPerSource)
        {
            if (buffer.Last is { } oldest)
            {
                buffer.RemoveLast();
                _seenKeys.TryRemove($"{oldest.Value.Link}-{oldest.Value.PubDate:yyyyMMddHHmm}", out _);
            }
        }

        // 7. حذف بافر خالی (اختیاری)
        if (buffer.Count == 0)
            _sourceBuffers.TryRemove(source, out _);

        // 8. اضافه به صف انتشار (موازی با مصرف)
        if (!_publishChannel.Writer.TryWrite(item))
        {
            await _publishChannel.Writer.WriteAsync(item, ct);
        }
    }

    /// <summary>
    /// چک وجود منبع دیگر در صف — بدون خالی کردن صف (Thread-Safe)
    /// </summary>
    public bool HasOtherSourceSafe(string currentSource)
    {
        var temp = new List<NewsItem>();
        while (_publishChannel.Reader.TryRead(out var item))
        {
            temp.Add(item);
            if (item.SourceName != currentSource)
            {
                // بازگرداندن همه آیتم‌ها
                foreach (var i in temp)
                    _publishChannel.Writer.TryWrite(i);
                return true;
            }
        }
        foreach (var i in temp)
            _publishChannel.Writer.TryWrite(i);
        return false;
    }

    /// <summary>
    /// عقب انداختن خبر (برای موازی‌سازی)
    /// </summary>
    public async ValueTask RepublishAsync(NewsItem item, CancellationToken ct = default)
    {
        await _publishChannel.Writer.WriteAsync(item, ct);
    }

    /// <summary>
    /// به‌روزرسانی آخرین منبع منتشرشده
    /// </summary>
    public void UpdateLastPublished(string sourceName, DateTimeOffset publishedAt)
    {
        LastSourcePublished = sourceName;
        LastPublishedAt = publishedAt;
    }

    /// <summary>
    /// دریافت کپی از صف فعلی — بدون اختلال در مصرف‌کننده
    /// </summary>
    public IReadOnlyList<NewsItem> GetCurrentPublishQueue()
    {
        var list = new List<NewsItem>();
        while (_publishChannel.Reader.TryRead(out var item))
        {
            list.Add(item);
        }
        foreach (var item in list)
        {
            _publishChannel.Writer.TryWrite(item);
        }
        return list;
    }

    /// <summary>
    /// پایان دادن به کانال
    /// </summary>
    public void Complete() => _publishChannel.Writer.Complete();
}