using System.Text.RegularExpressions;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public static class TextCleaner
    {
        private static readonly string[] KnownSources =
        {
            "خبرورزشی", "خبر ورزشی", "ورزش سه", "ورزش‌سه",
            "varzesh3", "khabarvarzeshi", "طرفداری", "tarafdari",
            "فوتبال", "فوتبالی", "فوتبالفوری", "فوتبال ۳۶۰"
        };

        public static string CleanTitle(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "خبر فوری";

            var text = input.Trim();

            // فقط حذف منبع از اول عنوان — همین و بس
            foreach (var source in KnownSources)
            {
                text = Regex.Replace(text,
                    $@"^{Regex.Escape(source)}\s*[:|\/\-–—]\s*",
                    "",
                    RegexOptions.IgnoreCase);
            }

            // حذف پیشوند کانال مثل "ورزش سه | "
            text = Regex.Replace(text, @"^[^\n|:]{2,40}[\|:]\s*", "");

            // حذف منشن و هشتگ
            text = Regex.Replace(text, @"@\w+", "");
            text = Regex.Replace(text, @"#\S+", "");

            // حذف کاراکترهای مخفی و فاصله اضافی
            text = Regex.Replace(text, @"[\u200B-\u200D\uFEFF\u2066-\u2069\u202A-\u202E]", "");
            text = Regex.Replace(text, @"\s{2,}", " ").Trim();

            return text;
        }

        public static string CleanShortDescription(string input, string title = "")
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            var text = input;

            // حذف منشن، هشتگ، کاراکترهای مخفی
            text = Regex.Replace(text, @"@\w+", "");
            text = Regex.Replace(text, @"#\S+", "");
            text = Regex.Replace(text, @"[\u200B-\u200D\uFEFF\u2066-\u2069\u202A-\u202E]", "");
            text = Regex.Replace(text, @"\s{2,}", " ");

            // حذف عنوان تکراری از اول متن
            if (!string.IsNullOrEmpty(title))
            {
                var cleanTitle = CleanTitle(title);
                if (text.TrimStart().StartsWith(cleanTitle, StringComparison.OrdinalIgnoreCase))
                {
                    text = text.Substring(cleanTitle.Length).Trim();
                }
            }

            return text.Trim();
        }
    }
}