using HtmlAgilityPack;

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
        /// واکشی HTML و بازگرداندن متن کامل خبر
        /// </summary>
        public async Task<string> GetFullDescriptionAsync(string url)
        {
            try
            {
                var html = await _httpClient.GetStringAsync(url);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // پیدا کردن محتوای اصلی، بر اساس تگ‌های متداول مقالات
                var contentNode = doc.DocumentNode.SelectSingleNode(
                    "//article | //div[contains(@class,'content')] | //div[contains(@class,'article-body')]");

                if (contentNode == null)
                    contentNode = doc.DocumentNode; // fallback به کل سند

                // پاکسازی تگ‌های غیر ضروری
                CleanNode(contentNode);

                var text = contentNode.InnerText;
                return NormalizeText(text);
            }
            catch (Exception ex)
            {
                // در صورت خطا، پیام خطا ثبت شود
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
            // حذف اسکریپت‌ها، استایل‌ها و iframe
            var unwantedTags = node.SelectNodes(".//script | .//style | .//iframe | .//noscript");
            if (unwantedTags != null)
            {
                foreach (var tag in unwantedTags)
                {
                    tag.Remove();
                }
            }
        }

        /// <summary>
        /// نرمال سازی متن: حذف فاصله‌های اضافی و خطوط خالی
        /// </summary>
        private string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Trim())
                            .Where(l => !string.IsNullOrEmpty(l));

            return string.Join(Environment.NewLine + Environment.NewLine, lines);
        }

        /// <summary>
        /// تبدیل URL نسبی به کامل
        /// </summary>
        private string NormalizeUrl(string baseUrl, string src)
        {
            if (Uri.TryCreate(src, UriKind.Absolute, out var absoluteUri))
                return absoluteUri.ToString();

            if (Uri.TryCreate(new Uri(baseUrl), src, out var relativeUri))
                return relativeUri.ToString();

            return src; // fallback
        }
    }
}
