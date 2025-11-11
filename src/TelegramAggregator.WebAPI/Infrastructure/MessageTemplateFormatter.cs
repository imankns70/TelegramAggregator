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

        private static string CleanFullDescription(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // حذف URL ها
            text = Regex.Replace(text, @"https?:\/\/\S+", "", RegexOptions.IgnoreCase);

            // حذف خطوط تبلیغاتی یا عبارت‌های غیرخبری
            string[] unwantedPhrases =
            {
        "شروع دعوت دوباره یادآوری کن",
        "پخش زنده",
        "دانلود اپلیکیشن",
        "تماس با ما",
        "ورزش سه",
        "طرفداری",
        "کد خبر",
        "ورود | ثبت نام",
        "تبلیغات",
        "خبر بعدی",
        "اپلیکیشن طرفداری"
    };

            foreach (var phrase in unwantedPhrases)
                text = text.Replace(phrase, "", StringComparison.OrdinalIgnoreCase);

            // حذف خطوط کوتاه و بی‌محتوا
            text = string.Join(Environment.NewLine,
                text.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 5));

            // حذف فاصله‌های اضافی
            text = Regex.Replace(text, @"\s+", " ");

            // متن تا آخرین نقطه نگه‌داشته شود
            var lastDotIndex = text.LastIndexOf('。') >= 0 ? text.LastIndexOf('。') : text.LastIndexOf('.');
            if (lastDotIndex > 0)
                text = text[..(lastDotIndex + 1)];

            return text.Trim();
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
