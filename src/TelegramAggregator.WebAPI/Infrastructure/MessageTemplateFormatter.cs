using System.Text;
using System.Text.RegularExpressions;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Services;

namespace TelegramAggregator.Services
{
    public static class MessageTemplateFormatter
    {
        private const string ChannelName = "@FOOTBALFORI";
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
        public static (string Caption, InlineKeyboardMarkup Keyboard) GetPreviewMessageWithInlineButton(NewsItem item, string botUsername, bool isProduction)
        {
            var title = string.IsNullOrWhiteSpace(item.Title) ? "خبر فوری" : CleanText(item.Title);
            var shortDesc = string.IsNullOrWhiteSpace(item.ShortDescription) ? "" : CleanText(item.ShortDescription);
            string fullDesc = CleanFullDescription(item.FullDescription, item.Title);


            // ساخت خلاصه هوشمند (برای پیش‌نمایش)
            string preview = SmartPreview(fullDesc);
            string callbackId = string.Empty;

            // ذخیره برای callback
            //callbackId = TelegramUpdateService.StoreForExpand(item);


            callbackId = HandleExpandCore.StoreForExpand(item);

            //callbackId = TelegramUpdateService.StoreForExpand(item);

            // کپشن نهایی با فرمت دلخواه
            var caption = FormatedMessage(title, shortDesc, preview);

            // دکمه InlineKeyboard برای نمایش متن کامل
            //var keyboard = new InlineKeyboardMarkup(
            //           InlineKeyboardButton.WithUrl(
            //                   "متن کامل خبر",
            //                   $"https://t.me/{botUsername}?start=expand_{callbackId.Replace("exp_", "")}"));

            var keyboard = new InlineKeyboardMarkup(
                       InlineKeyboardButton.WithCallbackData("متن کامل خبر", callbackId));

            return (caption, keyboard);
        }
        private static string FormatedMessage(string title, string shortDesc, string preview)
        {
            var sb = new StringBuilder();


            sb.AppendLine($"<b>{EscapeHtml(title)}</b>");

            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(shortDesc))
                sb.AppendLine(EscapeHtml(shortDesc));

            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(preview))
                sb.AppendLine(EscapeHtml(preview));

            sb.AppendLine();
            sb.AppendLine($"{ChannelName}");
            sb.AppendLine($"{Admin}");

            return sb.ToString().Trim();
        }
        public static string SmartPreview(string text, int limit = 200)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length <= limit)
                return text;

            int cutoff = text.LastIndexOfAny(new[] { '.', '!', '?', '؟' }, limit);
            if (cutoff < limit / 2) // جمله خیلی کوتاه نباشد
                cutoff = text.LastIndexOf(' ', limit);

            if (cutoff < 0)
                cutoff = limit;

            return text[..cutoff].Trim() + "…";
        }

        private static string CleanFullDescription(string text, string? title = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            // حذف لینک‌ها و نویزهای متنی
            text = Regex.Replace(text, @"https?:\/\/\S+", "", RegexOptions.IgnoreCase);

            // حذف | اضافی
            text = Regex.Replace(text, @"^\s*\|\s*", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"\s*\|\s*", " ");

            // حذف اطلاعات اضافی مثل "خبر: 527,101 ..." یا "صفحه اصلی ..."
            text = Regex.Replace(text, @"خبر\s*[:：].*?\d+", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"صفحه اصلی.*", "", RegexOptions.IgnoreCase);

            // حذف عنوان خبر تکراری داخل متن
            if (!string.IsNullOrWhiteSpace(title))
                text = Regex.Replace(text, Regex.Escape(title), "", RegexOptions.IgnoreCase);
            // حذف کامل هر چیزی که با "منبع:" شروع می‌شه (حتی اگه جمله اصلی تکرار شده باشه)
            text = Regex.Replace(text, @"منبع\s*[:：]\s*.+", "", RegexOptions.IgnoreCase);

            // حذف خط خالی یا خطی که فقط "منبع:" داره
            text = Regex.Replace(text, @"^\s*منبع\s*[:：]?\s*$\n?", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            // حذف «طرفداری | » و «ورزش سه | » اول متن (با هر نوع فاصله و نیم‌فاصله)
            text = Regex.Replace(text, @"^(طرفداری|ورزش\s*سه)\s*[\|｜:：]\s*", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);

            // اگه یه خط کامل فقط شامل «طرفداری | » یا «ورزش سه | » باشه (مثلاً خط اول خالی باشه)
            text = Regex.Replace(text, @"^\s*(طرفداری|ورزش\s*سه)\s*[\|｜:：]?\s*$\n?", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            // حذف الگوهای گزارش
            text = Regex.Replace(text,
                @"\b(به\s*گزارش|طبق\s*گزارش|بر\s*اساس\s*گزارش|از\s*گزارش|گزارش)\s+[\""'«»]?\s*[\u0600-\u06FFa-zA-Z0-9\s‌\-–_]*[\""'«»]?\s*[,:،]?",
                "", RegexOptions.IgnoreCase);

            // فقط کافیه این یه خط رو اول CleanFullDescription بذاری
            text = Regex.Replace(text, @"^\s*[-–—―−]\s*", "", RegexOptions.Multiline);
            // حذف عبارت‌های تبلیغاتی یا بی‌ربط
            string[] unwantedPhrases =
            {
                "شروع دعوت دوباره یادآوری کن", "پخش زنده", "دانلود اپلیکیشن", "تماس با ما",
                "ورزش سه", "طرفداری", "کد خبر", "ورود | ثبت نام", "تبلیغات",
                "خبر بعدی", "اپلیکیشن طرفداری", "به جمع ما", "لینک کانال",
                "کانال تلگرامی", "همراه ما باشید", "برای مشاهده اخبار بیشتر",
                "مشاهده سایر اخبار","خبر ورزشی","خبرورزشی",
                "صفحات اول روزنامه‌های ورزشی اروپا را هرروز در کیوسک طرفداری بخوانید"
            };

            foreach (var phrase in unwantedPhrases)
                text = Regex.Replace(text, phrase, "", RegexOptions.IgnoreCase);

            // حذف الگوهای آخر متن مثل لینک کانال
            text = Regex.Replace(text, @"(به\s*کانال.*|عضو\s*کانال.*|لینک\s*کانال.*|در\s*تلگرام\s*ما.*)$", "", RegexOptions.IgnoreCase);

            // حذف خطوط کوتاه و خالی
            text = string.Join(Environment.NewLine,
                text.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 5));

            // فشرده‌سازی فاصله‌ها
            text = Regex.Replace(text, @"\s+", " ");

            // پایان‌بندی طبیعی جمله‌ها
            var lastDotIndex = text.LastIndexOf('。') >= 0 ? text.LastIndexOf('。') : text.LastIndexOf('.');
            if (lastDotIndex > 0)
                text = text[..(lastDotIndex + 1)];

            if (!text.EndsWith('.'))
                text += ".";

            return text.Trim();
        }

        private static string EscapeHtml(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
        /// <summary>
        /// متن کامل — وقتی کاربر روی دکمه کلیک کرد
        /// </summary>
        public static string GetFullCaption(NewsItem item)
        {
            var title = string.IsNullOrWhiteSpace(item.Title) ? "خبر فوری" : CleanText(item.Title);
            var shortDesc = string.IsNullOrWhiteSpace(item.ShortDescription) ? "" : CleanText(item.ShortDescription);
            var fullDesc = string.IsNullOrWhiteSpace(item.FullDescription)
                ? "متن کامل در دسترس نیست."
                : CleanText(item.FullDescription);

            fullDesc = SmartTruncate(fullDesc);
            var caption = FormatedMessage(title, shortDesc, fullDesc);

            return caption;
        }


        private static string SmartTruncate(string text, int maxLength = 1024)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Length <= maxLength) return text;

            var cutoff = text.LastIndexOfAny(new[] { '.', '!', '؟', '?', '!' }, maxLength - 1);
            if (cutoff <= 0)
                cutoff = text.LastIndexOf(' ', maxLength - 1);

            if (cutoff <= 0)
                cutoff = maxLength - 1;

            return text.Substring(0, cutoff + 1).Trim();
        }
    }
}
