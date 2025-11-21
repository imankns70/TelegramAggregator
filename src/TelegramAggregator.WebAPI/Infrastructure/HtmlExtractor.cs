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
            try
            {
                var html = await _httpClient.GetStringAsync(url, ct);
                return html.Contains("video") || html.Contains("mp4") || html.Contains("aparat.com");
            }
            catch { return false; }
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

                var text = HtmlEntity.DeEntitize(contentNode.InnerText);
                text = Regex.Replace(text, @"\s{3,}", " ");
                text = Regex.Replace(text, @"[\u200B-\u200F\uFEFF]", "");
                text = ApplySiteSpecificCleanup(text, url);

                return text.Length > 80 ? text.Trim() : "متن خبر کوتاه است.";
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

                var host = new Uri(url).Host.ToLowerInvariant();
                var candidates = new List<ImageCandidate>();

                // 1. og:image و twitter:image — اولویت اول
                var og = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image' or @name='og:image']")
                           ?.GetAttributeValue("content", null);
                if (TryAddImage(og, url, 10000, candidates)) return candidates.First().Url;

                var twitter = doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:image' or @property='twitter:image']")
                                 ?.GetAttributeValue("content", null);
                if (TryAddImage(twitter, url, 9000, candidates)) return candidates.First().Url;

                // 2. عکس‌های داخل محتوا
                var imgNodes = doc.DocumentNode.SelectNodes(
                    "//article//img | //figure//img | //div[contains(@class,'news')]//img | " +
                    "//div[contains(@class,'content')]//img | //img[contains(@class,'main') or contains(@class,'lead')]"
                );

                if (imgNodes != null)
                {
                    foreach (var img in imgNodes)
                    {
                        var src = img.GetAttributeValue("src", "") ??
                                  img.GetAttributeValue("data-src", "") ??
                                  img.GetAttributeValue("data-lazy-src", "") ??
                                  img.GetAttributeValue("data-original", "") ??
                                  img.GetAttributeValue("srcset", "").Split(' ', ',')[0];

                        if (string.IsNullOrWhiteSpace(src)) continue;

                        var fullUrl = NormalizeUrl(url, src.Trim());
                        if (!IsValidMainImage(fullUrl)) continue;

                        var score = CalculateImageScore(fullUrl, img, host);
                        candidates.Add(new ImageCandidate(fullUrl, score));
                    }
                }

                return candidates.OrderByDescending(c => c.Score).FirstOrDefault()?.Url;
            }
            catch
            {
                return null;
            }
        }

        private record ImageCandidate(string Url, int Score);

        private bool TryAddImage(string? url, string baseUrl, int score, List<ImageCandidate> list)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            var normalized = NormalizeUrl(baseUrl, url.Trim());
            if (!IsValidMainImage(normalized)) return false;
            list.Add(new ImageCandidate(normalized, score));
            return true;
        }

        private int CalculateImageScore(string url, HtmlNode img, string host)
        {
            int score = 100;
            var lower = url.ToLowerInvariant();

            var sizeMatch = Regex.Match(lower, @"(\d{3,})x(\d{3,})");
            if (sizeMatch.Success && int.TryParse(sizeMatch.Groups[1].Value, out var w))
                score += w * 2;

            if (lower.Contains("main") || lower.Contains("lead") || lower.Contains("hero") || lower.Contains("large")) score += 800;
            if (lower.Contains("inline") && host.Contains("varzesh3")) score += 2000;
            if (lower.Contains("upload") && (host.Contains("tarafdari") || host.Contains("90tv"))) score += 1500;

            var classAttr = (img.GetAttributeValue("class", "") + img.GetAttributeValue("id", "")).ToLowerInvariant();
            if (classAttr.Contains("main") || classAttr.Contains("lead") || classAttr.Contains("photo")) score += 1000;

            var bad = new[] { "logo", "avatar", "icon", "thumb", "small", "advert", "banner" };
            if (bad.Any(b => lower.Contains(b))) score -= 5000;

            return Math.Max(score, 0);
        }

        private HtmlNode? SelectContentNode(HtmlDocument doc, string host)
        {
            var selectors = new[]
            {
                "//article",
                "//div[@itemprop='articleBody']",
                "//div[contains(@class,'news-body') or contains(@class,'story-body')]",
                "//div[contains(@class,'content') or contains(@class,'text') or contains(@class,'body')]",
                "//div[contains(@class,'item-body')]"
            };

            foreach (var sel in selectors)
            {
                var node = doc.DocumentNode.SelectSingleNode(sel);
                if (node != null && node.InnerText.Trim().Length > 150)
                    return node;
            }

            // درست و بدون خطا
            return doc.DocumentNode.SelectNodes("//div | //section | //main | //article")
                ?.Where(n => !string.IsNullOrWhiteSpace(n.InnerText))
                ?.Where(n => n.InnerText.Length > 300)
                ?.OrderByDescending(n => n.InnerText.Length)
                ?.ThenByDescending(n => n.XPath.Contains("article") ? 1 : 0)
                ?.FirstOrDefault()
                ?? doc.DocumentNode.SelectSingleNode("//body");
        }

        private void CleanNode(HtmlNode node)
        {
            if (node == null) return;
            var junk = new[] { "//script", "//style", "//iframe", "//noscript", "//div[contains(@class,'ad')]", "//aside", "//footer", "//nav" };
            foreach (var sel in junk)
            {
                foreach (var n in node.SelectNodes(sel) ?? Enumerable.Empty<HtmlNode>())
                    n.Remove();
            }
        }

        private string ApplySiteSpecificCleanup(string text, string url)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var host = new Uri(url).Host.ToLowerInvariant();

            // ۱. طرفداری — قبلاً داشتیم، بهترش کردیم
            if (host.Contains("tarafdari.com"))
            {
                text = string.Join("\n\n", text.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => !Regex.IsMatch(l, @"(مشاهده|دیدگاه|\d{1,2}/\d{1,2}/\d{4}|ترجمه|داغ‌ترین‌ها|منبع:.*?(دیلی|آس|گاردین|میل|بی‌بی‌سی)|تصاویری از|ویدئویی از)", RegexOptions.IgnoreCase))
                    .Where(l => l.Length > 20)
                );
            }
            // ۲. ورزش سه — قبلاً داشتیم، بهترش کردیم
            else if (host.Contains("varzesh3.com"))
            {
                text = Regex.Replace(text, @"(کد خبر.*|ورود\s*\|\s*ثبت نام|باشگاه خبرنگاران|برای ادامه مطلب.*|ورزش سه -.*)", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            }
            // ۳. خبرورزشی — جدید و ۱۰۰٪ تمیز!
            else if (host.Contains("khabarvarzeshi.com") || host.Contains("khabarvarzeshi.ir"))
            {
                var lines = text.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .ToList();

                var cleaned = new List<string>();

                foreach (var line in lines)
                {
                    // حذف خطوط کاملاً مزاحم خبرورزشی
                    bool isTrash = false;

                    // ۱. خط نویسنده + تاریخ + مشاهده + دیدگاه
                    if (Regex.IsMatch(line, @"\d{1,2}\/\d{1,2}\/\d{4}") ||
                        line.Contains("مشاهده") ||
                        line.Contains("دیدگاه") ||
                        line.Contains("نظرات") ||
                        line.Contains("بازدید"))
                        isTrash = true;

                    // ۲. خطوط معروف تبلیغاتی خبرورزشی
                    else if (line.Contains("دانلود اپلیکیشن") ||
                             line.Contains("ورود به سایت") ||
                             line.Contains("صفحه اصلی") ||
                             line.Contains("خبر بعدی") ||
                             line.Contains("خبر قبلی") ||
                             line.Contains("اخبار پیشنهادی") ||
                             line.Contains("پرطرفدارترین اخبار") ||
                             line.Contains("آخرین اخبار") ||
                             line.Contains("تبلیغات"))
                        isTrash = true;

                    // ۳. خطوط خیلی کوتاه یا فقط عدد/ساعت
                    else if (line.Length < 25 ||
                             Regex.IsMatch(line, @"^\d{1,2}:\d{2}$") ||
                             Regex.IsMatch(line, @"^[\d\.\-\s]+$"))
                        isTrash = true;

                    // ۴. منبع خارجی یا مترجم
                    else if (line.Contains("منبع:") && !line.Contains("خبرورزشی") ||
                             line.Contains("مترجم") ||
                             line.Contains("ترجمه") ||
                             line.Contains("گردآوری"))
                        isTrash = true;

                    if (!isTrash)
                        cleaned.Add(line);
                }

                text = string.Join("\n\n", cleaned);
            }

            return text.Trim();
        }

        private bool IsValidMainImage(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            var lower = url.ToLowerInvariant();
            var bad = new[] { "logo", "avatar", "icon", "banner", "ad", "advert", "thumb", "150x", "100x", "favicon" };
            return !bad.Any(b => lower.Contains(b)) &&
                   (lower.EndsWith(".jpg") || lower.EndsWith(".jpeg") || lower.EndsWith(".png") || lower.EndsWith(".webp"));
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