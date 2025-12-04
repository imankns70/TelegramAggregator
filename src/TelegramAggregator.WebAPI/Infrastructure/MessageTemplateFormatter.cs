using System.Text;
using System.Text.RegularExpressions;
using TelegramAggregator.Infrastructure;
using TelegramAggregator.WebAPI.Models;
using System.Linq;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public static class MessageTemplateFormatter
    {
        private const string ChannelName = "@FOOTBALFORI";
        private const string Admin = "#iman";
        private const int MaxCaptionLength = 1024;

        public static string GetCaption(NewsItem item)
        {
            item.Title = CleanText(item.Title);
            item.ShortDescription = CleanText(item.ShortDescription);
            item.FullDescription = CleanFullDescription(item.FullDescription, item.Title);

            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(item.Title))
                sb.AppendLine($"<b>{EscapeHtml(item.Title)}</b>");

            if (!string.IsNullOrWhiteSpace(item.ShortDescription))
            {
                sb.AppendLine();
                sb.AppendLine(EscapeHtml(item.ShortDescription));
            }

            string footer = $"\n{ChannelName}\n{Admin}";

            // توجه: عنوان و شرح کوتاه که در sb قرار دارند، همان چیزی هستند که طول واقعی کپشن را اشغال می کنند
            string truncatedFullDesc = SmartTruncateFullDescription(item.FullDescription, sb.ToString(), footer, MaxCaptionLength);

            if (!string.IsNullOrWhiteSpace(truncatedFullDesc))
            {
                sb.AppendLine();
                sb.AppendLine(EscapeHtml(truncatedFullDesc));
            }

            sb.Append(footer);

            return sb.ToString().Trim();
        }

        /// <summary>
        ///  توضیحات کامل را از انتها به سمت ابتدا بررسی می‌کند و فقط جملاتِ واقعی (پایان‌یافته با . ! ؟ ?) را انتخاب می‌کند
        ///  تا در مجموع با عنوان/شرح کوتاه/فوتر از maxLength تجاوز نکند.
        ///  اگر هیچ جملهٔ پایان‌یافته‌ای وجود نداشت یا هیچ‌کدام به تنهایی جا نشدند، یک تکهٔ پایانیِ متن (بدون اضافه کردن نقطه) برمی‌گرداند.
        /// </summary>
        private static string SmartTruncateFullDescription(string fullDescription, string titleShortDesc, string footer, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(fullDescription)) return string.Empty;

            // طولی که عنوان+شرح کوتاه+فوتر اشغال می‌کنند (همان چیزی که در خروجی واقعی خواهد بود)
            int reservedLength = titleShortDesc.Length + footer.Length;
            int allowedLength = maxLength - reservedLength;
            if (allowedLength <= 0) return string.Empty;

            // 1) تلاش برای استخراج جملات واقعی که با یکی از علامت‌های پایان جمله تمام شده‌اند
            var punctuatedMatches = Regex.Matches(fullDescription, @"[^\.!\?؟]+[\.!\?؟]")
                                         .Cast<Match>()
                                         .Select(m => m.Value.Trim())
                                         .ToArray();

            // جمع‌آوری جملات از آخر به اول (اولویت جمله‌های انتهایی)
            if (punctuatedMatches.Length > 0)
            {
                var selected = new List<string>();
                int totalSelectedLength = 0;

                for (int i = punctuatedMatches.Length - 1; i >= 0; i--)
                {
                    var sentence = punctuatedMatches[i];
                    // طول جمله + یک فاصله (برای جدا کردن جملات هنگام join)
                    int lenWithSep = sentence.Length + (selected.Count > 0 ? 1 : 0);

                    if (totalSelectedLength + lenWithSep > allowedLength)
                    {
                        // اگر جمله فعلی اضافه شود از محدودیت عبور می‌کنیم => نپذیر
                        // به جای برش جمله، به جمله قبل‌تر می‌رویم (طبق خواسته‌ات)
                        continue;
                    }

                    // در صورتی که جا بیفتد، آن را در ابتدای لیست (چون از آخر انتخاب شده) قرار می‌دهیم
                    selected.Insert(0, sentence);
                    totalSelectedLength += lenWithSep;
                }

                if (selected.Count > 0)
                {
                    // join با یک فاصله؛ توجه: جملات انتخابی قبلاً علامت پایان را دارند، پس نیازی به اضافه کردن نقطه نیست
                    return string.Join(" ", selected).Trim();
                }

                // اگر هیچ جملهٔ تکی هم جا نشد (یعنی همه جملات طولانی‌تر از allowedLength هستند)
                // سعی می‌کنیم از آخرین جملهٔ انتهایی یک تکهٔ کوتاه برداریم (بدون اضافه کردن نقطه)
                var lastSentence = punctuatedMatches.Last();
                if (lastSentence.Length <= allowedLength)
                {
                    return lastSentence; // این حالت بعید است چون قبلا چک کردیم، اما برای اطمینان
                }

                // برش منطقی از ابتدای جمله (می‌تونیم انتخاب کنیم که از ابتدا برش بزنیم)
                // ولی چون از آخر متن باید حرکت کنیم، بهتر است از **ابتدای همان جمله** تا allowedLength را بگیریم
                var truncated = lastSentence.Substring(0, Math.Min(allowedLength, lastSentence.Length)).Trim();
                return truncated;
            }

            // 2) اگر هیچ جملهٔ پایانی‌یافته‌ای پیدا نشد:
            // تلاش می‌کنیم بخش پایانی متن را (از انتها) طوری برداریم که معنی کمتری از بین برود:
            // - برش از انتهای متن به سمت جلو تا allowedLength، سپس برش تا یک مرز واژه (space) از ابتدا
            // - اینطوری از برش وسط کلمه تا حد امکان جلوگیری می‌شود. هیچ نقطه‌ای اضافه نمی‌کنیم.

            var normalized = fullDescription.Trim();

            if (normalized.Length <= allowedLength)
            {
                // کل متن جا می‌شود
                return normalized;
            }

            // برش انتهایی: برداشتن بخش پایانی متن به طول allowedLength
            var suffix = normalized.Substring(normalized.Length - allowedLength, allowedLength).Trim();

            // تلاش برای برش به جلو تا اولین فاصله (برای حفظ ابتدای کلمه)
            var firstSpaceIndex = suffix.IndexOf(' ');
            if (firstSpaceIndex > 0 && firstSpaceIndex < suffix.Length - 1)
            {
                // برش از آنجا به انتها (یعنی از اولین فضای پیدا شده +1 تا انتها)
                var candidate = suffix.Substring(firstSpaceIndex + 1).Trim();
                if (!string.IsNullOrEmpty(candidate))
                    return candidate;
            }

            // اگر نشد، همان suffix را برگردان
            return suffix;
        }

        private static string EscapeHtml(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }

        private static string CleanText(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            var clean = Regex.Replace(input, "<.*?>", " ");
            clean = Regex.Replace(clean, "&[a-zA-Z]+;", " ");
            clean = Regex.Replace(clean, @"http[s]?://\S+", " ");
            clean = Regex.Replace(clean, @"\s{2,}", " ").Trim();
            return clean;
        }

        private static string CleanFullDescription(string text, string? title = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            // حذف لینک‌ها و نویزها
            text = Regex.Replace(text, @"https?:\/\/\S+", "", RegexOptions.IgnoreCase);

            // حذف | اضافی
            text = Regex.Replace(text, @"^\s*\|\s*", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"\s*\|\s*", " ");

            // حذف الگوهای اطلاعاتی
            text = Regex.Replace(text, @"خبر\s*[:：].*?\d+", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"صفحه اصلی.*", "", RegexOptions.IgnoreCase);

            // حذف تکرار عنوان داخل متن
            if (!string.IsNullOrWhiteSpace(title))
                text = Regex.Replace(text, Regex.Escape(title), "", RegexOptions.IgnoreCase);

            // حذف الگوهای گزارش
            text = Regex.Replace(text,
                @"\b(به\s*گزارش|طبق\s*گزارش|بر\s*اساس\s*گزارش|از\s*گزارش|گزارش)\s+[\""'«»]?\s*[\u0600-\u06FFa-zA-Z0-9\s‌\-–_]*[\""'«»]?\s*[,:،]?",
                "", RegexOptions.IgnoreCase);

            // حذف عبارات تبلیغاتی شناخته‌شده
            string[] unwantedPhrases =
            {
                "شروع دعوت دوباره یادآوری کن", "پخش زنده", "دانلود اپلیکیشن", "تماس با ما",
                "ورزش سه", "طرفداری", "کد خبر", "ورود | ثبت نام", "تبلیغات",
                "خبر بعدی", "اپلیکیشن طرفداری", "به جمع ما", "لینک کانال",
                "کانال تلگرامی", "همراه ما باشید", "برای مشاهده اخبار بیشتر",
                "مشاهده سایر اخبار",
                "صفحات اول روزنامه‌های ورزشی اروپا را هرروز در کیوسک طرفداری بخوانید"
            };
            foreach (var phrase in unwantedPhrases)
                text = Regex.Replace(text, phrase, "", RegexOptions.IgnoreCase);

            // حذف الگوهای انتهایی مثل لینک کانال
            text = Regex.Replace(text, @"(به\s*کانال.*|عضو\s*کانال.*|لینک\s*کانال.*|در\s*تلگرام\s*ما.*)$", "", RegexOptions.IgnoreCase);

            // حذف خطوط کوتاه و خالی
            text = string.Join("\n",
                text.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 5));

            // فشرده‌سازی فاصله‌ها
            text = Regex.Replace(text, @"\s+", " ").Trim();

            // **نکته مهم:** اینجا دیگر هیچ نقطه مصنوعی اضافه نمی‌کنیم.
            return text;
        }
    }
}
