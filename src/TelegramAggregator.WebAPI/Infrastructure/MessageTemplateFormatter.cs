using System.Text;
using System.Text.RegularExpressions;
using TelegramAggregator.Infrastructure;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public static class MessageTemplateFormatter
    {
        public static async Task<string> PrepareMessageAsync(NewsItem item, ChannelConfig channel, HtmlExtractor htmlExtractor)
        {
            // پر کردن اطلاعات تکمیلی از لینک
            item.ImageUrl = await htmlExtractor.ExtractMainImageAsync(item.Link);
            item.FullDescription = await htmlExtractor.GetFullDescriptionAsync(item.Link);

            // تمیز کردن متن‌ها
            item.Title = CleanText(item.Title);
            item.ShortDescription = CleanText(item.ShortDescription);
            item.FullDescription = CleanFullDescription(item.FullDescription);

            // ساخت قالب پیام و برش هوشمند
            var caption = FormatMessage(item, channel);
            caption = SmartTruncate(caption);

            return caption;
        }

        private static string FormatMessage(NewsItem item, ChannelConfig channel)
        {
            var sb = new StringBuilder();

            // عنوان
            if (!string.IsNullOrWhiteSpace(item.Title))
                sb.AppendLine(EscapeHtml(item.Title));

            sb.AppendLine();

            // توضیح کوتاه
            if (!string.IsNullOrWhiteSpace(item.ShortDescription))
                sb.AppendLine(EscapeHtml(item.ShortDescription));

            sb.AppendLine();

            // شرح کامل
            if (!string.IsNullOrWhiteSpace(item.FullDescription))
            {
                // همه متن نمایش داده می‌شود، تلگرام خودش Show more اضافه می‌کند اگر طولانی باشد
                sb.AppendLine(EscapeHtml(item.FullDescription));
            }

            sb.AppendLine();

            // اطلاعات کانال و ادمین
            sb.AppendLine($"@{channel.ChannelUsername}");
            sb.AppendLine($"#{channel.AdminUsername}");

            return sb.ToString().Trim();
        }
        private static string FormatAdminMessage(string adminText, ChannelConfig channel)
        {
            var sb = new StringBuilder();

            // متن ادمین
            sb.AppendLine(CleanText(adminText));
            sb.AppendLine();

            // اطلاعات کانال و اکانت
            sb.AppendLine($"@{channel.ChannelUsername}");
            sb.AppendLine($"#{channel.AdminUsername}");

            return sb.ToString().Trim();
        }

        private static string EscapeHtml(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return input
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }
        private static string CleanText(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            // حذف تگ‌های HTML
            var clean = Regex.Replace(input, "<.*?>", " ");

            // حذف HTML entities
            clean = Regex.Replace(clean, "&[a-zA-Z]+;", " ");

            // حذف لینک‌ها
            clean = Regex.Replace(clean, @"http[s]?://\S+", " ");

            // پاکسازی فاصله‌های اضافی
            clean = Regex.Replace(clean, @"\s{2,}", " ").Trim();

            return clean;
        }

        private static string CleanFullDescription(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            var clean = input;

            // حذف تگ‌های HTML
            clean = Regex.Replace(clean, "<.*?>", " ");

            // حذف HTML entities
            clean = Regex.Replace(clean, "&[a-zA-Z]+;", " ");

            // حذف لینک‌ها
            clean = Regex.Replace(clean, @"http[s]?://\S+", " ");

            // حذف عبارت‌های مربوط به منبع خبر با حالت‌های مختلف
            clean = Regex.Replace(clean, @"به گزارش\s+[""']?\s*[\u0600-\u06FFa-zA-Z0-9\s‌\-–_]+[""']?\s*[,،:]?", " ", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"طبق گزارش\s+[""']?\s*[\u0600-\u06FFa-zA-Z0-9\s‌\-–_]+[""']?\s*[,،:]?", " ", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"از گزارش\s+[""']?\s*[\u0600-\u06FFa-zA-Z0-9\s‌\-–_]+[""']?\s*[,،:]?", " ", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"گزارش\s+[""']?\s*[\u0600-\u06FFa-zA-Z0-9\s‌\-–_]+[""']?\s*[,،:]?", " ", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\s*[""']?(ورزش سه|طرفداری|بی بی سی|فارس|تسنیم|ایسنا|مهر)[""']?\s*[،,:]?", " ", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"منبع\s*[:：]\s*[\u0600-\u06FFa-zA-Z0-9\s‌\-–_]+", " ", RegexOptions.IgnoreCase);

            // حذف فاصله‌های تکراری
            clean = Regex.Replace(clean, @"\s{2,}", " ").Trim();

            // حذف نقل‌قول‌های تکی در ابتدای پاراگراف‌ها
            clean = Regex.Replace(clean, @"^[\""'«»]+", "", RegexOptions.Multiline);

            return clean;
        }
        private static string SmartTruncate(string text, int maxLength = 1024)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Length <= maxLength) return text;

            // پیدا کردن آخرین علامت پایان جمله قبل از maxLength
            var cutoff = text.LastIndexOfAny(new[] { '.', '!', '؟', '?', '!' }, maxLength - 1);
            if (cutoff <= 0)
                cutoff = text.LastIndexOf(' ', maxLength - 1); // اگه جمله کامل پیدا نشد، تا آخرین فاصله ببریم

            if (cutoff <= 0)
                cutoff = maxLength - 1; // fallback

            return text.Substring(0, cutoff + 1).Trim();
        }

    }
}
