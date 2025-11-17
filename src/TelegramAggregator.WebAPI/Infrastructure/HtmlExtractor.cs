using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace TelegramAggregator.Infrastructure
{
    public class HtmlExtractor : IHtmlExtractor
    {
        private readonly HttpClient _httpClient;

        public HtmlExtractor(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient("HtmlExtractor");
        }

        // چک کردن وجود ویدیو (برای حذف کامل خبرهای ویدیویی)
        public async Task<bool> HasVideoAsync(string url, CancellationToken ct = default)
        {
            try
            {
                var html = await _httpClient.GetStringAsync(url, ct);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                return doc.DocumentNode.SelectSingleNode(
                    "//video | " +
                    "//iframe[contains(@src,'youtube') or contains(@src,'aparat') or contains(@src,'vimeo')] | " +
                    "//div[contains(@class,'video') or contains(@class,'player') or contains(@class,'embed')] | " +
                    "//meta[@property='og:video' or @name='twitter:player']"
                ) != null;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> GetFullDescriptionAsync(string url, CancellationToken ct = default)
        {
            try
            {
                var html = await _httpClient.GetStringAsync(url, ct);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var host = new Uri(url).Host.ToLowerInvariant();
                var contentNode = SelectContentNode(doc, host);

                if (contentNode == null)
                    return "متن خبر یافت نشد.";

                CleanNode(contentNode);

                // چک ویدیو
                if (CheckForVideo(doc))
                    return "این خبر حاوی ویدیو است و طبق تنظیمات ارسال نمی‌شود.";

                var rawText = contentNode.InnerText;
                var cleanText = NormalizeText(rawText);

                // پاکسازی خاص هر سایت
                cleanText = host.Contains("tarafdari.com") ? Regex.Replace(cleanText, @"(@|#)\w+|با دعوت.*|هدیه.*|تبدیل.*تومان.*", "", RegexOptions.IgnoreCase) :
                            host.Contains("varzesh3.com") ? Regex.Replace(cleanText, @"کد خبر.*|ورود\s*\|\s*ثبت نام.*|برای مشاهده.*", "", RegexOptions.IgnoreCase) :
                            host.Contains("khabarvarzeshi.com") ? Regex.Replace(cleanText, @"خبر[:：]\s*\d+.*?دقیقه|صفحه اصلی.*?-|نام نویسنده.*:", "", RegexOptions.IgnoreCase) :
                            cleanText;

                return cleanText.Length > 50 ? cleanText.Trim() : "متن خبر کوتاه یا نامشخص است.";
            }
            catch (Exception ex)
            {
                return $"[خطا در استخراج متن] {ex.Message}";
            }
        }

        public async Task<string?> ExtractMainImageAsync(string url, CancellationToken ct = default)
        {
            try
            {
                var html = await _httpClient.GetStringAsync(url, ct);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var host = new Uri(url).Host.ToLowerInvariant();

                // 1. متا تگ og:image
                var ogImage = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")
                    ?.GetAttributeValue("content", null);

                if (!string.IsNullOrWhiteSpace(ogImage) && IsValidMainImage(ogImage))
                    return NormalizeUrl(url, ogImage.Trim());

                // 2. img با اولویت بالا
                var candidates = doc.DocumentNode.SelectNodes(
                    "//article//img | " +
                    "//div[contains(@class,'content')]//img | " +
                    "//div[contains(@class,'body')]//img | " +
                    "//figure//img"
                );

                if (candidates != null)
                {
                    foreach (var img in candidates)
                    {
                        var src = img.GetAttributeValue("src", null) ??
                                  img.GetAttributeValue("data-src", null) ??
                                  img.GetAttributeValue("data-original", null) ??
                                  img.GetAttributeValue("data-lazy-src", null);

                        if (!string.IsNullOrWhiteSpace(src) && IsValidMainImage(src))
                            return NormalizeUrl(url, src.Trim());
                    }
                }
            }
            catch { /* ignore */ }

            return null;
        }

        private bool CheckForVideo(HtmlDocument doc)
        {
            return doc.DocumentNode.SelectSingleNode(
                "//video | //iframe | //div[contains(@class,'video')] | //meta[@property='og:video']"
            ) != null;
        }

        private HtmlNode? SelectContentNode(HtmlDocument doc, string host)
        {
            return host switch
            {
                var h when h.Contains("varzesh3.com") =>
                    doc.DocumentNode.SelectSingleNode("//div[@itemprop='articleBody'] | //div[contains(@class,'news-body')] | //div[contains(@class,'text')]"),

                var h when h.Contains("tarafdari.com") =>
                    doc.DocumentNode.SelectSingleNode("//div[contains(@class,'node-content')] | //div[contains(@class,'field--name-body')]"),

                var h when h.Contains("khabarvarzeshi.com") =>
                    doc.DocumentNode.SelectSingleNode("//div[contains(@class,'story-body')] | //div[contains(@class,'item-body')] | //div[contains(@class,'story')]"),

                _ => doc.DocumentNode.SelectSingleNode("//article | //div[@itemprop='articleBody'] | //div[contains(@class,'content')]")
            };
        }

        private void CleanNode(HtmlNode node)
        {
            if (node == null) return;

            foreach (var tag in new[] { "script", "style", "nav", "header", "footer", "aside", "form", "iframe", "noscript", "advert", "ad" })
            {
                foreach (var n in node.SelectNodes($"//{tag}") ?? Enumerable.Empty<HtmlNode>())
                    n.Remove();
            }

            foreach (var n in node.SelectNodes("//*[contains(@class,'share') or contains(@class,'social') or contains(text(),'اپلیکیشن') or contains(text(),'دانلود اپ')]")
                     ?? Enumerable.Empty<HtmlNode>())
            {
                n.Remove();
            }
        }
       
        private string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            text = HtmlEntity.DeEntitize(text);
            text = text.Replace("\u200C", " ").Replace("\u200B", "").Replace("\u200D", "").Replace("\uFEFF", "");
            text = Regex.Replace(text, @"^[\|\->\s]+", "");
            text = Regex.Replace(text, @"\s{2,}", " ");

            var lines = text.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 10)
                .Take(25);

            return string.Join("\n\n", lines).Trim();
        }

        private bool IsValidMainImage(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            var lower = url.ToLowerInvariant();

            var bad = new[] { "logo", "avatar", "icon", "banner", "header", "footer", "ad", "advert", "telegram", "placeholder", "thumb", "150x150", "100x100" };
            if (bad.Any(b => lower.Contains(b))) return false;

            return lower.Contains(".jpg") || lower.Contains(".jpeg") || lower.Contains(".png") || lower.Contains(".webp");
        }

        // متد جا افتاده قبلی — حالا هست!
        private string NormalizeUrl(string baseUrl, string src)
        {
            if (string.IsNullOrWhiteSpace(src)) return src;
            if (Uri.TryCreate(src, UriKind.Absolute, out var absoluteUri))
                return absoluteUri.ToString();

            var baseUri = new Uri(baseUrl);
            if (Uri.TryCreate(baseUri, src, out var relativeUri))
                return relativeUri.ToString();

            return src;
        }
    }
}