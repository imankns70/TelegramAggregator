using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace TelegramAggregator.Infrastructure
{
    public class HtmlExtractor
    {
        private readonly HttpClient _httpClient;

        public HtmlExtractor(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        /// <summary>
        /// واکشی HTML و استخراج محتوای اصلی خبر (متن تمیزشده)
        /// </summary>
        public async Task<string> GetFullDescriptionAsync(string url)
        {
            try
            {
                var html = await _httpClient.GetStringAsync(url);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // 🔍 تلاش برای یافتن بخش اصلی مقاله با XPathهای متداول
                var contentNode = doc.DocumentNode.SelectSingleNode(
                    "//article" +
                    " | //div[contains(@class,'content')]" +
                    " | //div[contains(@class,'article-body')]" +
                    " | //div[contains(@class,'post-body')]" +
                    " | //div[contains(@class,'entry-content')]" +
                    " | //div[contains(@class,'news-body')]" +     // مخصوص ورزش سه
                    " | //div[contains(@id,'main-content')]" +
                    " | //section[contains(@class,'main-content')]" // مخصوص برخی سایت‌ها
                );

                if (contentNode == null)
                {
                    // fallback: جستجو برای طولانی‌ترین div از نظر متن
                    contentNode = doc.DocumentNode
                        .SelectNodes("//div")
                        ?.OrderByDescending(n => n.InnerText.Length)
                        .FirstOrDefault()
                        ?? doc.DocumentNode;
                }

                // 🧹 حذف تگ‌های غیرضروری
                CleanNode(contentNode);

                // 📝 استخراج متن تمیز
                var text = contentNode.InnerText;

                return NormalizeText(text);
            }
            catch (Exception ex)
            {
                return $"[Error extracting content] {ex.Message}";
            }
        }


        public async Task<string?> ExtractMainImageAsync(string url)
        {
            try
            {
                var html = await _httpClient.GetStringAsync(url);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // ۱. تلاش برای گرفتن og:image (تصویر شاخص استاندارد)
                var imgNode = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']");

                // ۲. اگر پیدا نشد، اولین تصویر داخل مقاله یا محتوای اصلی
                if (imgNode == null)
                {
                    imgNode = doc.DocumentNode.SelectSingleNode(
                        "//article//img | //div[contains(@class,'content')]//img | //div[contains(@class,'article-body')]//img");
                }

                if (imgNode != null)
                {
                    var imgUrl = imgNode.GetAttributeValue("content", null) ?? imgNode.GetAttributeValue("src", null);
                    if (!string.IsNullOrWhiteSpace(imgUrl))
                    {
                        return NormalizeUrl(url, imgUrl);
                    }
                }
            }
            catch
            {
                // اگر خطا رخ داد، null برمی‌گردانیم
            }

            return null;
        }

        /// <summary>
        /// پاکسازی تگ‌های غیرضروری از یک Node
        /// </summary>
        private void CleanNode(HtmlNode node)
        {
            // حذف تگ‌های اضافی
            var tagsToRemove = new[] { "script", "style", "nav", "header", "footer", "form", "aside" };
            foreach (var tag in tagsToRemove)
            {
                var nodes = node.SelectNodes($"//{tag}");
                if (nodes == null) continue;
                foreach (var n in nodes)
                    n.Remove();
            }

            // حذف لینک‌ها و تبلیغات بی‌ربط
            var adNodes = node.SelectNodes("//*[contains(text(),'اپلیکیشن') or contains(text(),'پخش زنده') or contains(text(),'پادکست')]");
            if (adNodes != null)
            {
                foreach (var ad in adNodes)
                    ad.Remove();
            }
        }


        /// <summary>
        /// نرمال سازی متن: حذف فاصله‌های اضافی و خطوط خالی
        /// </summary>
        /// <summary>
        /// نرمال‌سازی متن: حذف فاصله‌های اضافی، کاراکترهای کنترلی و خطوط خالی
        /// </summary>
        private string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // تبدیل HTML entities و حذف فاصله‌های خاص
            text = HtmlEntity.DeEntitize(text)
                             .Replace("\t", " ")
                             .Replace("\u200C", "") // حذف نیم‌فاصله zero-width
                             .Replace("\u200B", ""); // حذف zero-width space

            // حذف فاصله‌های متوالی
            text = Regex.Replace(text, @"\s{2,}", " ");

            // حذف خطوط خالی و فاصله‌های اضافه‌ی اطراف
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Trim())
                            .Where(l => !string.IsNullOrEmpty(l));

            return string.Join(Environment.NewLine + Environment.NewLine, lines).Trim();
        }


        /// <summary>
        /// تبدیل URL نسبی به کامل
        /// </summary>
        private string NormalizeUrl(string baseUrl, string src)
        {
            if (Uri.TryCreate(src, UriKind.Absolute, out var absoluteUri))
                return absoluteUri.ToString();

            // اطمینان از اینکه baseUrl با '/' تمام می‌شود
            if (!baseUrl.EndsWith("/"))
                baseUrl += "/";

            if (Uri.TryCreate(new Uri(baseUrl), src, out var relativeUri))
                return relativeUri.ToString();

            return src; // fallback
        }

    }
}
