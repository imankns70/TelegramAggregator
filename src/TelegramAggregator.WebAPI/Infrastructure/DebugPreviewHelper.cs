// فایل: Services/DebugPreviewHelper.cs
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.Services;

namespace TelegramAggregator.Services
{
    public static class DebugPreviewHelper
    {
        public static string GenerateTelegramPreview(NewsItem item)
        {
            var cleanText = string.IsNullOrWhiteSpace(item.FullDescription)
                ? $"{item.Title}\n\n[متن خبر در دسترس نیست]"
                : $"{item.Title}\n\n{item.FullDescription.Trim()}";

            var finalCaption = cleanText + "\n\n@FOOTBALFORI #iman";

            // همون SmartTruncate که توی ربات استفاده می‌کنی
           /// finalCaption = MessageTemplateFormatter.SmartTruncate(finalCaption, 1015);

            return finalCaption
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\n", "<br>");
        }
    }
}