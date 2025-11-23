using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.Services
{
    public static class MessageTemplateFormatter
    {
        private const string ChannelHandle = "@FOOTBALFORI";
        private const string Hashtag = "#iman";
        private const int PreviewLength = 130;

        public static string GetCleanText(NewsItem item)
        {
            string title = item.Title?.Trim() ?? "بدون عنوان";
            string desc = item.FullDescription?.Trim();
            if (string.IsNullOrWhiteSpace(desc))
                desc = "[متن خبر در دسترس نیست]";
            return $"{title}\n\n{desc}";
        }

        public static InlineKeyboardMarkup GetFullNewsButton(string url)
        {
            return new InlineKeyboardMarkup(
                InlineKeyboardButton.WithUrl("مشاهده خبر کامل", url)
            );
        }

        public static string GetFooter() => $"\n\n{ChannelHandle} {Hashtag}";

        public static string GetFullCaption(NewsItem item, bool addFooter = true)
        {
            var title = item.Title?.Trim() ?? "بدون عنوان";
            var desc = item.FullDescription?.Trim() ?? "[متن کامل خبر در دسترس نیست]";
            var fullText = $"{title}\n\n{desc}";
            if (addFooter) fullText += GetFooter();
            return SmartTruncate(fullText, 4000);
        }

        // نسخه اصلی که با photo کار می‌کنه
        public static (string Caption, InlineKeyboardMarkup Keyboard) GetCollapsibleCaption(NewsItem item, InputFileUrl photo)
        {
            string fullText = GetCleanText(item);

            // اگر متن کوتاه بود → بدون دکمه
            if (fullText.Length <= PreviewLength + 50)
            {
                return (fullText + GetFooter(), GetFullNewsButton(item.Link));
            }

            // کوتاه‌سازی هوشمند
            string previewText = fullText;
            if (previewText.Length > PreviewLength)
            {
                int cut = previewText.LastIndexOfAny(new[] { '۔', '.', '!', '?', '\n', ' ' }, PreviewLength);
                cut = cut > 50 ? cut : PreviewLength;
                previewText = previewText.Substring(0, cut + 1).TrimEnd();
                if (!"۔.!?\n".Contains(previewText[^1]))
                    previewText += "…";
            }

            string shortCaption = $"{previewText}\n\n[نمایش ادامه خبر]\n{GetFooter()}";

            // ذخیره در ExpandStorage و دریافت callbackId
            string callbackData = TelegramUpdateService.StoreForExpand(item, photo);

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("نمایش ادامه خبر", callbackData) },
                new[] { InlineKeyboardButton.WithUrl("مشاهده خبر کامل", item.Link) }
            });

            return (shortCaption, keyboard);
        }

        // نسخه نهایی که در SendToTelegramAsync استفاده می‌شه
        public static (string Caption, InlineKeyboardMarkup Keyboard) GetFinalMessage(NewsItem item, InputFileUrl photo)
        {
            return GetCollapsibleCaption(item, photo);
        }

        // وقتی کاربر کلیک کرد → متن کامل
        public static string GetExpandedCaption(NewsItem item)
        {
            return GetFullCaption(item) + "\n\n[متن کامل نمایش داده شد]";
        }

        public static string SmartTruncate(string text, int maxLength = 1015)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
                return text;

            string footer = GetFooter();
            int available = maxLength - footer.Length - 10;
            if (available < 50) return text.Substring(0, maxLength - footer.Length) + footer;

            int cut = text.LastIndexOfAny(new[] { '۔', '.', '!', '?', '\n' }, available);
            if (cut < 0) cut = available;

            string result = text.Substring(0, cut + 1).TrimEnd();
            if (!"۔.!?".Contains(result[^1])) result += "…";
            return result + footer;
        }
    }
}