using System.Text;
using System.Text.RegularExpressions;
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Models.TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public static class MessageTemplateFormatter
    {
        public static string FormatMessage(NewsItem item, ChannelConfig channel, bool includeFullDescription = true)
        {
            var sb = new StringBuilder();

            // عنوان با بولد
            sb.AppendLine($"📰 <b>{CleanText(item.Title)}</b>");
            sb.AppendLine();

            // توضیح کوتاه
            if (!string.IsNullOrWhiteSpace(item.ShortDescription))
            {
                sb.AppendLine($"📖 {CleanText(item.ShortDescription)}");
                sb.AppendLine();
            }

            // شرح کامل (در صورت وجود و درخواست)
            if (includeFullDescription && !string.IsNullOrWhiteSpace(item.FullDescription))
            {
                sb.AppendLine($"📖 {CleanText(item.FullDescription)}");
                sb.AppendLine();
            }

            // اطلاعات کانال و ادمین
            sb.AppendLine($"@{channel.ChannelUsername}");
            sb.AppendLine($"#{channel.AdminUsername}");

            return sb.ToString().Trim();
        }
        public static string FormatAdminMessage(string adminText, ChannelConfig channel)
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
    }
}
