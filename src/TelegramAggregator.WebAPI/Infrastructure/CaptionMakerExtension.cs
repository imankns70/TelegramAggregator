using System.Text;
using System.Text.RegularExpressions;
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Models.Entities;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public static class CaptionMakerExtension
    {
        private const string Admin = "#iman";
        private const int MaxCaptionLength = 1024;

        public static string GetCaption(News item)
        {
            // تمیز کردن عنوان و شرح کوتاه
            var title = CleanText(item.Title ?? "خبر فوری");
            var shortDesc = CleanText(item.ShortDescription ?? "");

            var fullText = CleanFullDescription(item.FullDescription ?? "").Trim();

            var sb = new StringBuilder();

            // عنوان اصلی بولد
            sb.AppendLine($"<b>{Escape(title)}</b>");
            sb.AppendLine();
            sb.AppendLine($"{Escape(shortDesc)}");
            sb.AppendLine(); // خط خالی بعد از تیترها

            // فضای باقی‌مانده برای متن اصلی
            int reservedFooter = Admin.Length + 20;
            int available = MaxCaptionLength - sb.Length - reservedFooter;

            string bodyText;

            if (fullText.Length <= available)
            {
                bodyText = fullText;
            }
            else
            {
                int cutoff = FindLastCompleteSentence(fullText, available);
                if (cutoff < 150) cutoff = available - 40;
                bodyText = fullText.Substring(0, cutoff).Trim();
            }

            sb.Append(Escape(bodyText));

            // فوتر — بدون هیچ کلمه اضافه
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine(Admin);

            var result = sb.ToString();

            // بیمه نهایی
            if (result.Length > MaxCaptionLength)
            {
                int cut = MaxCaptionLength - reservedFooter - 10;
                int safeCut = cut > 150 ? cut : 150;
                result = result.Substring(0, safeCut).Trim()
                       + "\n\n" + Admin;
            }

            return result;
        }

        private static int FindLastCompleteSentence(string text, int maxLength)
        {
            if (text.Length <= maxLength) return text.Length;

            char[] endings = { '۔', '.', '!', '?', '؟', '\n' };

            for (int i = maxLength - 1; i >= 120; i--)
            {
                if (i < text.Length && endings.Contains(text[i]))
                    return i + 1;
            }

            int lastSpace = text.LastIndexOf(' ', maxLength - 20);
            if (lastSpace > 150) return lastSpace;

            return Math.Max(180, maxLength - 60);
        }

        private static string CleanText(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            return Regex.Replace(input, "<.*?>", " ")
                        .Replace("&nbsp;", " ")
                        .Replace("  ", " ")
                        .Trim();
        }

        private static string Escape(string text) =>
            text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static string CleanFullDescription(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            // ۱. حذف همه کاراکترهای مخفی
            text = Regex.Replace(text, @"[\u200B-\u200D\uFEFF\u2066-\u2069\u202A-\u202E]", "");

            // ۲. حذف لینک‌ها
            text = Regex.Replace(text, @"https?://\S+", "");

            // حذف دقیق و کامل "طرفداری |" — حتی اگه وسط متن باشه!
            text = Regex.Replace(text, @"طرفداری\s*[\|｜]\s*", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"ورزش\s*سه\s*[\|｜]\s*", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"خبرورزشی\s*[\|｜]\s*", "", RegexOptions.IgnoreCase);

            // حذف هر خطی که با منبع شروع بشه (طرفداری | ، ورزش سه | و …)
            text = Regex.Replace(text, @"^(طرفداری|ورزش\s*سه|خبرورزشی).*?[\|｜].*$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

            // حذف "به گزارش طرفداری"، "به گزارش ورزش سه" و …
            text = Regex.Replace(text, @"به\s*گزارش\s*.{0,30}?(طرفداری|ورزش\s*سه|خبرورزشی).*?[,،:]\s*", "", RegexOptions.IgnoreCase);

            // حذف هر خطی که فقط منبع باشه
            text = Regex.Replace(text, @"^[\w\s]*?(طرفداری|ورزش\s*سه|خبرورزشی)[\|｜:].*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);

            // حذف نویسنده و تاریخ و هر چیز قبل از متن اصلی
            text = Regex.Replace(text, @"^[\w\s\.\-–—آ-ی]+?[:\-–—]\s*", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"^[\w\s\.\-–—آ-ی]+?\s*[-–—]\s*", "", RegexOptions.Multiline);

            // حذف | و : و ؛ اول خط
            text = Regex.Replace(text, @"^\s*[\|｜:؛]\s*", "", RegexOptions.Multiline);

            // حذف خطوطی که شامل "مشاهده" یا "دیدگاه" باشن
            text = Regex.Replace(text, @".*?\d+[\s\u200C]*مشاهده.*", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @".*?\d+[\s\u200C]*دیدگاه.*", "", RegexOptions.Multiline);

            // حذف فاصله‌های اضافی
            text = Regex.Replace(text, @"\s{2,}", " ");

            return text.Trim();
        }
    }
}