using System;

namespace TelegramAggregator.WebAPI.Models;

/// <summary>
/// مدل خبر با تاریخ UTC، کلید دقیق و امنیت بالا
/// </summary>
public class NewsItem
{
    public long? Id { get; set; } 
    public int Score { get; set; } = 0;
    /// <summary>عنوان خبر یا پست</summary>
    public string Title { get; set; } = "";

    /// <summary>توضیح کوتاه یا خلاصه محتوا</summary>
    public string ShortDescription { get; set; } = "";

    /// <summary>شرح کامل محتوا</summary>
    public string FullDescription { get; set; } = "";

    /// <summary>لینک منبع اصلی</summary>
    public string Link { get; set; } = "";

    /// <summary>تاریخ انتشار محتوا (UTC)</summary>
    public DateTimeOffset PubDate { get; set; }

    /// <summary>لینک تصویر (در صورت وجود)</summary>
    public string? ImageUrl { get; set; }

    /// <summary>لینک ویدیو (در صورت وجود)</summary>
    public string? VideoUrl { get; set; }

    /// <summary>نوع محتوا: News, Video, Tweet, etc.</summary>
    public string Type { get; set; } = "News";

    /// <summary>نام منبع (مثلاً طرفداری، ورزش3 و ...)</summary>
    public string? SourceName { get; set; }

    /// <summary>پیام خطا (در صورت وجود)</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>کلید یکتا: منبع + لینک + تاریخ (دقیقه)</summary>
    public string UniqueKey =>
        $"{SourceName?.ToLowerInvariant()}-{Link.GetHashCode()}-{PubDate:yyyyMMddHHmm}";

    /// <summary>برای مقایسه دقیق در HashSet</summary>
    public override bool Equals(object? obj)
    {
        return obj is NewsItem other && UniqueKey == other.UniqueKey;
    }

    /// <summary>برای HashSet</summary>
    public override int GetHashCode() => UniqueKey.GetHashCode();
    
}