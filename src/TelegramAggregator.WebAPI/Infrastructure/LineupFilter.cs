using System.Text.RegularExpressions;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Services;

/// <summary>
/// فیلتر هوشمند اخبار ترکیب و پیش‌بازی
/// </summary>
public sealed class LineupFilter
{
    private readonly string[] _lineupKeywords;

    // Regexهای کامپایل‌شده برای حداکثر سرعت
    private static readonly Regex TitleLineupRegex = new(
        @"ترکیب\s+.+?\s+(و|با)\s+.+?\s+(مشخص|اعلام)\s+شد",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex PlayerListRegex = new(
        @"\b(\d{1,2}\.?\s*[-–]?\s*.+?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex PlayerRoleRegex = new(
        @"\b(دروازه.?بان|مدافع|هافبک|مهاجم|فوروارد|وینگر|مدافع کناری|هافبک دفاعی)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    public LineupFilter(string[] lineupKeywords)
    {
        _lineupKeywords = lineupKeywords ?? Array.Empty<string>();
    }

    /// <summary>
    /// آیا خبر مربوط به ترکیب یا پیش‌بازی است؟
    /// </summary>
    public bool IsLineupOrPreMatchNews(NewsItem item)
    {
        if (item == null) return false;

        var text = $"{item.Title} {item.ShortDescription} {item.FullDescription}"
                   .Replace("‌", " ")  // نیم‌فاصله
                   .Replace("ي", "ی")
                   .Replace("ك", "ک")
                   .ToLowerInvariant();

        // 1. کلیدواژه‌های دقیق
        if (_lineupKeywords.Any(k => text.Contains(k.ToLowerInvariant())))
            return true;

        // 2. الگوی عنوان
        if (TitleLineupRegex.IsMatch(item.Title))
            return true;

        // 3. ساختار لیست بازیکنان + نقش
        if (PlayerListRegex.IsMatch(text) && PlayerRoleRegex.IsMatch(text))
            return true;

        return false;
    }
}