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
        var lowerTitle = item.Title.ToLowerInvariant();
        var lowerText = $"{item.Title} {item.ShortDescription}".ToLowerInvariant();

        // فقط ترکیب‌های احتمالی، شایعه، پیش‌بینی و قبل از بازی رو رد کن
        var trashPatterns = new[]
        {
        "ترکیب احتمالی",
        "ترکیب پیش بینی",
        "ترکیب شایعه",
        "ترکیب ممکن",
        "ترکیب دلخواه",
        "ترکیب رویایی",
        "ترکیب ایده آل",
        "اگر من مربی بودم",
        "پیش بازی",
        "پیش از بازی",
        "قبل از بازی",
        "آماده سازی",
        "تمرین قبل از بازی",
        "لباس تمرینی",
        "کیت جدید",
        "پیراهن جدید",
        "لباس سوم",
        "لباس چهارم",
        "مارول",
        "ثور",
        "انتقام جویان",
        "لباس مارول"
    };

        if (trashPatterns.Any(p => lowerTitle.Contains(p.ToLowerInvariant()) ||
                                   lowerText.Contains(p.ToLowerInvariant())))
        {
            return true; // رد بشه
        }

        // ترکیب رسمی رو قبول کن (اینا خبر مهمن!)
        var officialPatterns = new[]
        {
        "ترکیب رسمی",
        "ترکیب اصلی",
        "۱۱ نفر اصلی",
        "ترکیب نهایی",
        "ترکیب اعلام شد",
        "ترکیب پرسپولیس",
        "ترکیب استقلال",
        "ترکیب تیم",
        "دقیقه ۹۰ ترکیب",
        "اعلام ترکیب"
    };

        if (officialPatterns.Any(p => lowerTitle.Contains(p.ToLowerInvariant())))
        {
            return false; // قبول بشه (حتی اگه بقیه فیلترها بزنن، این مهم‌تره!)
        }

        // اگه هیچ کدوم نبود → قبول بشه (مثل خبر "ثور به رئال اضافه شد")
        return false;
    }
}