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
            _httpClient.Timeout = TimeSpan.FromSeconds(20);
        }

        public async Task<bool> HasVideoAsync(string url, CancellationToken ct = default)
        {
            // الان غیرفعاله — فقط برای دیباگ
            return false;
        }

        public async Task<string> GetFullDescriptionAsync(string url, CancellationToken ct = default)
        {
            try
            {
                var html = await _httpClient.GetStringAsync(url, ct);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var contentNode = SelectContentNode(doc, new Uri(url).Host);
                if (contentNode == null || string.IsNullOrWhiteSpace(contentNode.InnerText.Trim()))
                    return "متن خبر کوتاه است.";

                CleanNode(contentNode);

                var rawText = contentNode.InnerText;
                var cleanText = NormalizeText(rawText);
                cleanText = ApplySiteSpecificCleanup(cleanText, url);

                return cleanText.Length > 80 ? cleanText.Trim() : "متن خبر کوتاه است.";
            }
            catch (Exception ex)
            {
                return $"[خطا] {ex.Message}";
            }
        }

        public async Task<string?> ExtractMainImageAsync(string url, CancellationToken ct = default)
        {
            try
            {
                var html = await _httpClient.GetStringAsync(url, ct);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var og = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", null);
                if (!string.IsNullOrWhiteSpace(og) && IsValidMainImage(og))
                    return NormalizeUrl(url, og.Trim());

                var twitter = doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:image']")?.GetAttributeValue("content", null);
                if (!string.IsNullOrWhiteSpace(twitter) && IsValidMainImage(twitter))
                    return NormalizeUrl(url, twitter.Trim());

                var imgs = doc.DocumentNode.SelectNodes("//article//img | //div[contains(@class,'content')]//img | //figure//img");
                if (imgs != null)
                {
                    foreach (var img in imgs)
                    {
                        var src = img.GetAttributeValue("src", "") ??
                                  img.GetAttributeValue("data-src", "") ??
                                  img.GetAttributeValue("data-original", "") ??
                                  img.GetAttributeValue("data-lazy-src", "");

                        if (!string.IsNullOrWhiteSpace(src) && IsValidMainImage(src))
                            return NormalizeUrl(url, src.Trim());
                    }
                }
            }
            catch { /* ignore */ }

            return null;
        }

        private HtmlNode? SelectContentNode(HtmlDocument doc, string host)
        {
            var selectors = new[]
            {
                "//article", "//div[@itemprop='articleBody']", "//div[contains(@class,'news-body')]",
                "//div[contains(@class,'story-body')]", "//div[contains(@class,'node-content')]",
                "//div[contains(@class,'field--name-body')]", "//div[contains(@class,'content')]",
                "//div[contains(@class,'text')]", "//div[contains(@class,'item-body')]"
            };

            foreach (var sel in selectors)
            {
                var node = doc.DocumentNode.SelectSingleNode(sel);
                if (node != null && node.InnerText.Trim().Length > 150)
                    return node;
            }

            var candidates = doc.DocumentNode.SelectNodes("//div | //section | //main");
            if (candidates != null)
            {
                return candidates
                    .Where(n => n.InnerText.Length > 300)
                    .OrderByDescending(n => n.InnerText.Length)
                    .FirstOrDefault();
            }

            return doc.DocumentNode.SelectSingleNode("//body");
        }

        private void CleanNode(HtmlNode node)
        {
            if (node == null) return;

            var junk = new[]
            {
                "//script", "//style", "//iframe", "//noscript",
                "//div[contains(@class,'ad')]", "//div[contains(@class,'advert')]",
                "//div[contains(@class,'share')]", "//div[contains(@class,'social')]",
                "//aside", "//footer", "//header", "//nav"
            };

            foreach (var sel in junk)
            {
                foreach (var n in node.SelectNodes(sel) ?? Enumerable.Empty<HtmlNode>())
                    n.Remove();
            }
        }

        private string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            text = HtmlEntity.DeEntitize(text);
            text = Regex.Replace(text, @"[\u200B-\u200F\uFEFF]", "");
            text = Regex.Replace(text, @"\s{3,}", " ");
            text = Regex.Replace(text, @"^[\|\->•★✔✨]\s*", "", RegexOptions.Multiline);

            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 25
                    && !Regex.IsMatch(l, @"^\w+\s+\w+\s+\d{1,2}\/\d{1,2}\/\d{4}")   // مهدی همدانی 11/18/2025
                    && !Regex.IsMatch(l, @"^\d{1,2}:\d{2}")                          // 20:13
                    && !l.Contains("مشاهده") && !l.Contains("دیدگاه") && !l.Contains("نظرات")
                    && !l.Contains("کد خبر") && !l.Contains("ورود") && !l.Contains("دانلود اپ")
                    && !l.Contains("منبع") && !l.Contains("طرفداری") && !l.Contains("ورزش۳"))
                .Take(40);

            return string.Join("\n\n", lines).Trim();
        }

        private string ApplySiteSpecificCleanup(string text, string url)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var host = new Uri(url).Host.ToLowerInvariant();

            // فقط برای طرفداری — روش خط به خط و کاملاً ایمن
            if (host.Contains("tarafdari.com"))
            {
                var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(l => l.Trim())
                                .Where(l => l.Length > 0)
                                .ToList();

                var cleaned = new List<string>();

                foreach (var line in lines)
                {
                    bool isTrash = false;

                    // ۱. خط نویسنده + تاریخ + مشاهده/دیدگاه (همیشه وجود داره!)
                    if (Regex.IsMatch(line, @"\d{1,2}/\d{1,2}/\d{4}") &&
                        (line.Contains("مشاهده") || line.Contains("دیدگاه") || line.Contains("نظرات")))
                        isTrash = true;

                    // ۲. خط مترجم/ترجمه (هر اسمی باشه می‌گیره!)
                    else if (Regex.IsMatch(line, @"^(ترجمه|ترجمه شده|مترجم|نویسنده)[:：]?\s", RegexOptions.IgnoreCase))
                        isTrash = true;

                    // ۳. تبلیغات معروف طرفداری
                    else if (line.Contains("داغترینها") ||
                             line.Contains("داغ‌ترین‌ها") ||
                             Regex.IsMatch(line, @"[↓👇]{2,}"))
                        isTrash = true;

                    // ۴. منبع خارجی + دیلی میل + آس + گاردین + تصاویری از
                    else if (line.StartsWith("دیلی میل", StringComparison.OrdinalIgnoreCase) ||
                             line.StartsWith("آس", StringComparison.OrdinalIgnoreCase) ||
                             line.StartsWith("گاردین", StringComparison.OrdinalIgnoreCase) ||
                             line.Contains("تصاویری از") ||
                             line.Contains("ویدئویی از") ||
                             (line.Contains("منبع:") && !Regex.IsMatch(line, @"منبع:\s*طرفداری", RegexOptions.IgnoreCase)))
                        isTrash = true;

                    // ۵. ساعت تنها یا خطوط خیلی کوتاه/فقط ایموجی
                    else if (line.Length < 18 ||
                             Regex.IsMatch(line, @"^\d{1,2}:\d{2}$") ||
                             Regex.IsMatch(line, @"^[\W_]+$"))
                        isTrash = true;

                    if (!isTrash)
                        cleaned.Add(line);
                }

                text = string.Join("\n\n", cleaned);
            }
            // سایت‌های دیگر
            else if (host.Contains("varzesh3.com"))
            {
                text = Regex.Replace(text, @"کد خبر.*|ورود\s*\|\s*ثبت نام.*|برای مشاهده.*|باشگاه خبرنگاران.*", "", RegexOptions.IgnoreCase);
            }
            else if (host.Contains("khabarvarzeshi.com"))
            {
                text = Regex.Replace(text, @"خبر[:：]\s*\d+.*?دقیقه|صفحه اصلی The End|نام نویسنده.*:", "", RegexOptions.IgnoreCase);
            }

            return text.Trim();
        }
        private bool IsValidMainImage(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            var lower = url.ToLowerInvariant();
            var bad = new[] { "logo", "avatar", "icon", "banner", "header", "footer", "ad", "advert", "telegram", "placeholder", "thumb", "150x", "100x", "favicon" };
            if (bad.Any(b => lower.Contains(b))) return false;
            return lower.EndsWith(".jpg") || lower.EndsWith(".jpeg") || lower.EndsWith(".png") || lower.EndsWith(".webp") || lower.EndsWith(".gif");
        }

        private string NormalizeUrl(string baseUrl, string src)
        {
            if (string.IsNullOrWhiteSpace(src)) return src;
            if (src.StartsWith("http")) return src;
            if (src.StartsWith("//")) return "https:" + src;
            return new Uri(new Uri(baseUrl), src).ToString();
        }
    }
}