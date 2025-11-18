using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.Services
{
    public static class MessageTemplateFormatter
    {
        private const string ChannelHandle = "@FOOTBALFORI";
        private const string Hashtag = "#iman";

        // فقط عنوان + متن (بدون منبع و چیز اضافه)
        public static string GetCleanText(NewsItem item)
        {
            string title = item.Title?.Trim() ?? "";
            string desc = item.FullDescription?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(desc))
                desc = "[متن خبر در دسترس نیست]";

            return $"{title}\n\n{desc}";
        }

        // دکمه لینک
        public static InlineKeyboardMarkup GetFullNewsButton(string url)
        {
            return new InlineKeyboardMarkup(
                InlineKeyboardButton.WithUrl("مشاهده خبر کامل", url)
            );
        }

        // فوتر (بدون خط جداکننده)
        public static string GetFooter()
        {
            return $"\n\n{ChannelHandle}\n{Hashtag}";
        }

        // کپشن کامل
        public static string GetFullCaption(NewsItem item, bool addFooter = true)
        {
            string text = GetCleanText(item);
            return addFooter ? text + GetFooter() : text;
        }

        /// <summary>
        /// کوتاه‌سازی هوشمند متن همراه با حفظ فوتر
        /// </summary>
        public static string SmartTruncate(string text, int maxLength = 1015)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            if (text.Length <= maxLength)
                return text;

            // چون دیگر separator نداریم، فوتر دقیقاً همین دو خط است
            string footer = $"\n\n{ChannelHandle}\n{Hashtag}";
            int footerLength = footer.Length;

            // اگر متن از سقف کمتر بود → برگردان
            if (text.Length <= maxLength)
                return text;

            int available = maxLength - footerLength - 5;
            if (available < 0) available = 0;

            // بریدن در محل مناسب
            int cut = text.LastIndexOfAny(
                new[] { '.', '!', '?', '\n', '۔', '؟' }, available
            );

            if (cut < 0) cut = available;

            string main = text.Substring(0, cut + 1).Trim();

            if (!".!?۔؟".Contains(main.LastOrDefault()))
                main += "…";

            return main + footer;
        }
    }
}
