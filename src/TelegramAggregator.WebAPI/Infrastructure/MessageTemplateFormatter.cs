using System.Text.RegularExpressions;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.Services
{
    public static class MessageTemplateFormatter
    {
        private const string ChannelHandle = "@FOOTBALFORI";
        private const string Admin = "#iman";

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

        /// <summary>
        /// پیش‌نمایش خبر در کانال + لینک "ادامه خبر" که کاربر رو میاره تو ربات
        /// </summary>
        public static (string Caption, InlineKeyboardMarkup Keyboard) GetPreviewMessageWithInlineButton(NewsItem item, string botUsername)
        {
            var title = string.IsNullOrWhiteSpace(item.Title) ? "خبر فوری" : CleanText(item.Title);
            var shortDesc = string.IsNullOrWhiteSpace(item.ShortDescription) ? "" : CleanText(item.ShortDescription);
            var fullDesc = string.IsNullOrWhiteSpace(item.FullDescription) ? "متن خبر در دسترس نیست." : CleanText(item.FullDescription);

            // ساخت خلاصه هوشمند (برای پیش‌نمایش)
            string preview = fullDesc;
            const int limit = 200;
            if (preview.Length > limit)
            {
                int cut = preview.IndexOfAny(new[] { '.', '!', '?', '\n' }, 120);
                if (cut < 0 || cut > limit) cut = limit;
                preview = preview[..cut].Trim() + "…";
            }

            // ذخیره برای callback
            var callbackId = TelegramUpdateService.StoreForExpand(item);

            // کپشن نهایی با فرمت دلخواه
            var caption = $"<b>{title}</b>\n\n" +
                          $"{shortDesc}\n\n" +
                          $"{preview}\n\n" +
                          $"{ChannelHandle}\n\n" +
                          $"{Admin}\n";

            // دکمه InlineKeyboard برای نمایش متن کامل
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                InlineKeyboardButton.WithCallbackData("متن کامل خبر", $"exp_{callbackId}")
            });

            return (caption, keyboard);
        }

        /// <summary>
        /// متن کامل — وقتی کاربر روی دکمه کلیک کرد
        /// </summary>
        public static string GetFullCaption(NewsItem item)
        {
            var title = string.IsNullOrWhiteSpace(item.Title) ? "خبر فوری" : CleanText(item.Title);
            var shortDesc = string.IsNullOrWhiteSpace(item.ShortDescription) ? "" : CleanText(item.ShortDescription);
            var fullDesc = string.IsNullOrWhiteSpace(item.FullDescription) ? "متن کامل در دسترس نیست." : CleanText(item.FullDescription);

            return $"<b>{title}</b>" +
                   (string.IsNullOrWhiteSpace(shortDesc) ? "" : $"\n\n{shortDesc}") +
                   $"\n\n\n{fullDesc}\n\n" +
                   $"{ChannelHandle}" +
                   $"{Admin}\n";
        }
    }
}
