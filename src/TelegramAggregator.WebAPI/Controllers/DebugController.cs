using Microsoft.AspNetCore.Mvc;
using TelegramAggregator.WebAPI.Infrastructure;

[ApiController]
[Route("api/debug")]
public class DebugApiController : ControllerBase
{
    private readonly DebugStorage _debugStorage;

    public DebugApiController(DebugStorage debugStorage)
    {
        _debugStorage = debugStorage;
    }
    [HttpGet("feed")]
    public IActionResult Feed()
    {
        return Ok(new
        {
            // ۱. صف فعلی انتشار
            currentQueue = _debugStorage.GetCurrentPublishQueue()
                .Select(x => new
                {
                    sourceName = x.SourceName ?? "نامشخص",
                    title = x.Title ?? "بدون عنوان",
                    pubDate = x.PubDate.ToString("o"),
                    x.PublishStatus
                })
                .ToList(),

            // ۲. آخرین خبرهای ارسال‌شده
            publishedItems = _debugStorage.GetPublishedItems()
                .Select(x => new
                {
                    sourceName = x.SourceName ?? "نامشخص",
                    title = x.Title ?? "بدون عنوان",
                    publishedAt = x.PublishedAt?.ToString("o"),
                    x.Link
                })
                .ToList(),

            // ۳. آخرین خبرهای پردازش‌شده (قبول + رد با دلیل!)
            recentProcessed = _debugStorage.GetAllItems()
    .OrderByDescending(x => x.ProcessedAt)
    .Take(40)
    .Select(x => new
    {
        sourceName = x.SourceName ?? "نامشخص",
        title = x.Title ?? "بدون عنوان",
        link = x.Link,                    // اینو اضافه کن!
        processedAt = x.ProcessedAt.ToString("o"),
        isAccepted = x.IsAccepted,
        rejectReason = x.RejectReason,
        publishStatus = x.PublishStatus
    })
    .ToList()
        });
    }

    [HttpGet("stats")]
    public IActionResult Stats()
    {
        var summary = _debugStorage.GetSummary();
        var now = DateTimeOffset.Now;

        // آمار صف با جزئیات منبع
        var queueBySource = _debugStorage.GetCurrentPublishQueue()
            .GroupBy(x => x.SourceName ?? "نامشخص")
            .Select(g => new { Source = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Source)
            .ToList();

        var queueDetails = queueBySource.Any()
            ? string.Join(" | ", queueBySource.Select(x => $"{x.Source} ({x.Count})"))
            : "خالی";

        var lastHourItems = _debugStorage.GetAllItems()
            .Where(x => x.ProcessedAt > now.AddHours(-1))
            .ToList();

        var todayAccepted = _debugStorage.GetAllItems()
            .Count(x => x.IsAccepted && x.ProcessedAt.Date == now.Date);

        var lastHourTotal = lastHourItems.Count;
        var lastHourAccepted = lastHourItems.Count(x => x.IsAccepted);
        var acceptanceRate = lastHourTotal > 0
            ? Math.Round((double)lastHourAccepted * 100 / lastHourTotal)
            : 0;

        return Ok(new
        {
            در_صف = summary.InQueueCount,
            جزئیات_صف = queueDetails,  // ← جدید و خفن!
            کل_ارسال_شده = summary.PublishedCount,
            ساعت_اخیر = new
            {
                کل = lastHourTotal,
                قبول = lastHourAccepted,
                رد = lastHourTotal - lastHourAccepted,
                درصد_قبولی = acceptanceRate
            },
            امروز = new { قبول = todayAccepted }
        });
    }
}