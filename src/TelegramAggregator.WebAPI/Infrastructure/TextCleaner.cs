using System.Text.RegularExpressions;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public static class TextCleaner
    {
        private static readonly Regex NbspRegex = new(@"\u00A0|&nbsp;?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HiddenCharsRegex = new(@"[\u200c\u200b\u200f\u202a-\u202e\u2066-\u2069]", RegexOptions.Compiled);
        private static readonly Regex ChannelPrefixRegex = new(@"^[^\n\|:]{2,30}[\|:]\s*", RegexOptions.Compiled);
        private static readonly Regex UsernameRegex = new(@"@\w+", RegexOptions.Compiled);
        private static readonly Regex HashtagRegex = new(@"#\S+", RegexOptions.Compiled);

        private static readonly Regex ExtraPunctuationRegex = new(@"[|\/\\+*#^<>""'‘’'«»{}[\]()]|\.{2,}|…", RegexOptions.Compiled);

        public static readonly string[] KnownSources =
        {
            "خبرورزشی", "خبر ورزشی", "ورزش سه", "ورزش‌سه", "varzesh3", "khabarvarzeshi",
            "طرفداری", "tarafdari", "فوتبال", "فوتبالی", "فوتبالفوری", "فوتبال ۳۶۰"
        };

        public static string CleanTitle(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            var text = input.Trim();

            // 1. حذف کاراکترهای مخفی و &nbsp;
            text = HiddenCharsRegex.Replace(text, "");
            text = NbspRegex.Replace(text, " ");

            // 2. حذف منبع و پیشوند کانال
            foreach (var source in KnownSources)
            {
                text = Regex.Replace(text, $"^{Regex.Escape(source)}\\s*[:\\-–—\\|]*\\s*", "", RegexOptions.IgnoreCase);
            }
            text = ChannelPrefixRegex.Replace(text, "");

            // 3. حذف منشن و هشتگ
            text = UsernameRegex.Replace(text, "");
            text = HashtagRegex.Replace(text, "");

            // 4. حذف علائم نگارشی اضافی
            text = ExtraPunctuationRegex.Replace(text, " ");

            // 5. حذف کلمات انتهایی تزئینی (عکس، فیلم، ویدیو و ...)
            text = Regex.Replace(text, @"\s*(عکس|فیلم|ویدیو|ویدئو|ویدیویی|تصویر|ویدیوها|عکس‌ها)\s*$", "", RegexOptions.IgnoreCase);

            // 6. اصلاح چسبندگی کلمات فارسی رایج
            text = FixCommonPersianSticking(text);

            // 7. نرمال‌سازی فاصله‌ها
            text = Regex.Replace(text, @"\s+", " ", RegexOptions.Singleline);

            // 8. نرمال‌سازی حروف
            text = text.Replace("ي", "ی").Replace("ك", "ک").Replace("ة", "ه").Replace("ؤ", "و");

            return text.Trim();
        }

        public static string CleanShortDescription(string input, string title = "")
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            var original = input;

            // اعمال همان تمیزکاری‌های عنوان (اما بدون حذف "عکس" در آخر)
            var text = original;
            text = HiddenCharsRegex.Replace(text, "");
            text = NbspRegex.Replace(text, " ");
            text = ExtraPunctuationRegex.Replace(text, " ");
            text = FixCommonPersianSticking(text);
            text = Regex.Replace(text, @"\s+", " ", RegexOptions.Singleline);
            text = text.Replace("ي", "ی").Replace("ك", "ک");

            // حذف عنوان از اول متن اگر تکرار شده
            if (!string.IsNullOrEmpty(title))
            {
                var cleanTitle = CleanTitle(title);
                if (text.TrimStart().StartsWith(cleanTitle, StringComparison.OrdinalIgnoreCase))
                {
                    text = text.Substring(cleanTitle.Length).TrimStart();
                }
            }

            // تقسیم بر اساس خطوط واقعی (نه فقط فضای خالی)
            var lines = original.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(l => CleanTitle(l)) // هر خط رو جداگانه تمیز کن
                                .Where(l => l.Length > 15)   // خطوط خیلی کوتاه (مثل "عکس") حذف بشن
                                .ToList();

            // فقط اولین خط معنی‌دار (لید خبر) رو برگردون
            return lines.Any() ? lines[0].Trim() : text.Trim();
        }

        // اصلاح چسبندگی‌های بسیار رایج در اخبار فارسی
        private static string FixCommonPersianSticking(string text)
        {
            var fixes = new (string pattern, string replacement)[]
            {
                (@"نقطهضعف", "نقطه ضعف"),
                (@"نقطه ضعف", "نقطه ضعف"), // دوباره برای اطمینان
                (@"فداکاری", "فداکاری"), // فعلاً دست نزن
                (@"بهشکل", "به شکل"),
                (@"تمامقد", "تمام قد"),
                (@"همهجانبه", "همه جانبه"),
                (@"نیمه‌نهایی", "نیمه نهایی"),
                (@"لیگبرتر", "لیگ برتر"),
                (@"جامحذفی", "جام حذفی"),
                (@"تیمملی", "تیم ملی"),
                (@"فدراسیونفوتبال", "فدراسیون فوتبال"),
                (@"سرمربی", "سرمربی"), // درست
                (@"بازیکن", "بازیکن"),
            };

            foreach (var (pattern, replacement) in fixes)
            {
                text = Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase);
            }

            return text;
        }
    }
}