using System.Collections.Concurrent;
using System.Threading.Channels;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure;

/// <summary>
/// ذخیره‌سازی هوشمند خبرها با چک منبع‌محور، شباهت و تاریخ
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
    private int MaxItemsPerSource = 0; // حداکثر خبر در حافظه هر منبع

    public string? LastSourcePublished { get; private set; }
    public DateTimeOffset? LastPublishedAt { get; private set; }
    public ChannelReader<NewsItem> PublishReader => _publishChannel.Reader;

    public NewsStorage(int maxItem)
    {
        MaxItemsPerSource= maxItem;
        _publishChannel = Channel.CreateBounded<NewsItem>(
            new BoundedChannelOptions(PublishQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true
            });
    }

    /// <summary>
    /// اضافه کردن خبر با چک منبع‌محور
    /// </summary>
    public async ValueTask AddAsync(NewsItem item, CancellationToken ct = default)
    {
        if (item?.Link is null || item.SourceName is null) return;

        var source = item.SourceName;
        var key = $"{item.Link}-{item.PubDate:yyyyMMddHHmm}";

        // 1. چک کلید سراسری
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
        if (buffer.Count > MaxItemsPerSource)
        {
            if (buffer.Last is { } oldest)
            {
                buffer.RemoveLast();
                _seenKeys.TryRemove($"{oldest.Value.Link}-{oldest.Value.PubDate:yyyyMMddHHmm}", out _);
            }
        }

        // 7. حذف بافر خالی
        if (buffer.Count == 0)
            _sourceBuffers.TryRemove(source, out _);

        // 8. اضافه به صف انتشار
        if (!_publishChannel.Writer.TryWrite(item))
        {
            await _publishChannel.Writer.WriteAsync(item, ct);
        }
    }

    /// <summary>
    /// چک وجود منبع دیگر در صف
    /// </summary>
    public bool HasOtherSource(string currentSource)
    {
        var reader = _publishChannel.Reader;
        while (reader.TryRead(out var item))
        {
            if (item.SourceName != currentSource)
                return true;
        }
        return false;
    }

    /// <summary>
    /// عقب انداختن خبر
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
    /// دریافت کپی از صف فعلی (بدون اختلال)
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

    public void Complete() => _publishChannel.Writer.Complete();
}