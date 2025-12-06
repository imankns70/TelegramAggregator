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

            // حذف منبع از اول عنوان
            foreach (var source in KnownSources)
            {
                text = Regex.Replace(text,
                    $@"^{Regex.Escape(source)}\s*[:|\/\-–—]\s*",
                    "",
                    RegexOptions.IgnoreCase);
            }

            text = Regex.Replace(text, @"^[^\n|:]{2,40}[\|:]\s*", "");
            text = Regex.Replace(text, @"@\w+", "");
            text = Regex.Replace(text, @"#\S+", "");

            // حذف نویسه‌های مخفی (ولی نیم‌فاصله رو هوشمندانه نگه دار!)
            text = Regex.Replace(text, @"[\u200B\u200D\uFEFF\u2066-\u2069\u202A-\u202E]", "");
            text = Regex.Replace(text, @"(\u200C)+", "\u200C"); // چند نیم‌فاصله → یکی
            text = Regex.Replace(text, @"^[\u200C\s]+|[\u200C\s]+$", ""); // اول و آخر خط
            text = Regex.Replace(text, @"[\u200C\s]{2,}", " "); // دو یا بیشتر → فاصله معمولی

            return text.Trim();
        }

        public static string CleanShortDescription(string input, string title = "")
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            var text = input;

            text = Regex.Replace(text, @"@\w+", "");
            text = Regex.Replace(text, @"#\S+", "");
            text = Regex.Replace(text, @"[\u200B\u200D\uFEFF\u2066-\u2069\u202A-\u202E]", "");
            text = Regex.Replace(text, @"(\u200C)+", "\u200C");
            text = Regex.Replace(text, @"^[\u200C\s]+|[\u200C\s]+$", "");
            text = Regex.Replace(text, @"[\u200C\s]{2,}", " ");

            if (!string.IsNullOrEmpty(title))
            {
                var cleanTitle = CleanTitle(title);
                if (text.TrimStart().StartsWith(cleanTitle, StringComparison.OrdinalIgnoreCase))
                {
                    text = text.Substring(cleanTitle.Length).TrimStart();
                }
            }

            return text.Trim();
        }
    }
}