using System.Text.RegularExpressions;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public static class TextSimilarity
    {
        /// <summary>
        /// بازمی‌گرداند میزان شباهت بین دو رشته در بازه 0..1 (1 = کاملاً مشابه).
        /// </summary>
        public static double GetSimilarity(string? a, string? b)
        {
            a ??= string.Empty;
            b ??= string.Empty;

            var na = Normalize(a);
            var nb = Normalize(b);

            if (na.Length == 0 && nb.Length == 0) return 1.0;
            if (na.Length == 0 || nb.Length == 0) return 0.0;

            var dist = LevenshteinDistance(na, nb);
            var maxLen = Math.Max(na.Length, nb.Length);

            return 1.0 - (double)dist / maxLen;
        }

        /// <summary>
        /// نرمال‌سازی متن برای مقایسه:
        /// - حذف تگ‌های HTML
        /// - حذف نمادها (غیرفونتیک)
        /// - تبدیل به حروف کوچک
        /// - فشرده‌سازی فاصله‌ها
        /// - تبدیل اعداد فارسی به انگلیسی
        /// </summary>
        public static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            // حذف تگ‌های HTML
            var s = Regex.Replace(input, "<.*?>", " ");

            // تبدیل به lower
            s = s.ToLowerInvariant();

            // تبدیل اعداد فارسی -> انگلیسی
            s = ReplacePersianDigits(s);

            // حذف علامت‌ها (در صورت نیاز می‌توان بعضی از نمادها را نگه داشت)
            s = Regex.Replace(s, @"[^\p{L}\p{Nd}\s]", " ");

            // فشرده‌سازی فضاها و trim
            s = Regex.Replace(s, @"\s+", " ").Trim();

            return s;
        }

        /// <summary>
        /// تبدیل اعداد فارسی و عربی به ارقام انگلیسی (۰۱۲۳۴۵۶۷۸۹ -> 0123456789)
        /// </summary>
        private static string ReplacePersianDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            s = s.Replace('۰', '0').Replace('۱', '1').Replace('۲', '2')
                 .Replace('۳', '3').Replace('۴', '4').Replace('۵', '5')
                 .Replace('۶', '6').Replace('۷', '7').Replace('۸', '8')
                 .Replace('۹', '9');

            // Arabic-Indic digits
            s = s.Replace('٠', '0').Replace('١', '1').Replace('٢', '2')
                 .Replace('٣', '3').Replace('٤', '4').Replace('٥', '5')
                 .Replace('٦', '6').Replace('٧', '7').Replace('٨', '8')
                 .Replace('٩', '9');

            return s;
        }

        /// <summary>
        /// محاسبه فاصله لِونسشتاین بین دو رشته (پیاده‌سازی استاندارد با استفاده از ماتریس).
        /// </summary>
        private static int LevenshteinDistance(string s, string t)
        {
            // اگر یکی خالی باشد
            if (string.IsNullOrEmpty(s)) return t.Length;
            if (string.IsNullOrEmpty(t)) return s.Length;

            var n = s.Length;
            var m = t.Length;

            // نگهداری تنها دو سطر برای حافظه کمتر
            var prev = new int[m + 1];
            var curr = new int[m + 1];

            // مقداردهی اولیه سطر صفر
            for (int j = 0; j <= m; j++)
                prev[j] = j;

            for (int i = 1; i <= n; i++)
            {
                curr[0] = i;
                char si = s[i - 1];

                for (int j = 1; j <= m; j++)
                {
                    char tj = t[j - 1];
                    int cost = (si == tj) ? 0 : 1;

                    int deletion = prev[j] + 1;
                    int insertion = curr[j - 1] + 1;
                    int substitution = prev[j - 1] + cost;

                    int value = deletion;
                    if (insertion < value) value = insertion;
                    if (substitution < value) value = substitution;

                    curr[j] = value;
                }

                // swap prev and curr
                var temp = prev;
                prev = curr;
                curr = temp;
            }

            return prev[m];
        }
    }
}
