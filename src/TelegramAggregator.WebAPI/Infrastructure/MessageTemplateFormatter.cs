using System.Text;
using System.Text.RegularExpressions;
using TelegramAggregator.Infrastructure;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public static class MessageTemplateFormatter
    {
        public static async Task<string> PrepareMessageAsync(NewsItem item, ChannelConfig channel, HtmlExtractor htmlExtractor)
        {
            // واکشی تصویر و متن کامل
            var imageTask = htmlExtractor.ExtractMainImageAsync(item.Link);
            var fullDescTask = htmlExtractor.GetFullDescriptionAsync(item.Link);
            await Task.WhenAll(imageTask, fullDescTask);

            item.ImageUrl = imageTask.Result;
            item.FullDescription = fullDescTask.Result;

            item.Title = CleanText(item.Title);
            item.ShortDescription = CleanText(item.ShortDescription);
            item.FullDescription = CleanFullDescription(item.FullDescription);

            var caption = FormatMessage(item, channel);
            return SmartTruncate(caption);
        }

        private static string FormatMessage(NewsItem item, ChannelConfig channel)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(item.Title))
                sb.AppendLine($"<b>{EscapeHtml(item.Title)}</b>");

            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(item.ShortDescription))
                sb.AppendLine(EscapeHtml(item.ShortDescription));

            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(item.FullDescription))
                sb.AppendLine(EscapeHtml(item.FullDescription));

            sb.AppendLine();
            sb.AppendLine($"@{channel.ChannelUsername}");
            sb.AppendLine($"#{channel.AdminUsername}");

            return sb.ToString().Trim();
        }

        private static string EscapeHtml(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
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

        private static string CleanFullDescription(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            text = Regex.Replace(text, @"https?:\/\/\S+", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text,
                @"\b(به\s*گزارش|طبق\s*گزارش|بر\s*اساس\s*گزارش|از\s*گزارش|گزارش)\s+[\""'«»]?\s*[\u0600-\u06FFa-zA-Z0-9\s‌\-–_]*[\""'«»]?\s*[,:،]?",
                "", RegexOptions.IgnoreCase);

            string[] unwantedPhrases =
            {
                "شروع دعوت دوباره یادآوری کن", "پخش زنده", "دانلود اپلیکیشن", "تماس با ما",
                "ورزش سه", "طرفداری", "کد خبر", "ورود | ثبت نام", "تبلیغات",
                "خبر بعدی", "اپلیکیشن طرفداری", "به جمع ما", "لینک کانال",
                "کانال تلگرامی", "همراه ما باشید", "برای مشاهده اخبار بیشتر",
                "مشاهده سایر اخبار"
            };

            foreach (var phrase in unwantedPhrases)
                text = Regex.Replace(text, phrase, "", RegexOptions.IgnoreCase);

            text = Regex.Replace(text, @"(به\s*کانال.*|عضو\s*کانال.*|لینک\s*کانال.*|در\s*تلگرام\s*ما.*)$", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"[""“”«»]{2,}", "", RegexOptions.IgnoreCase);

            text = string.Join(Environment.NewLine,
                text.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 5));

            text = Regex.Replace(text, @"\s+", " ");

            var lastDotIndex = text.LastIndexOf('。') >= 0 ? text.LastIndexOf('。') : text.LastIndexOf('.');
            if (lastDotIndex > 0)
                text = text[..(lastDotIndex + 1)];

            if (!text.EndsWith('.'))
                text += ".";

            return text.Trim();
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
