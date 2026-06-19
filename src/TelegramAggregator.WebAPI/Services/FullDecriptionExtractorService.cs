using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace TelegramAggregator.WebAPI.Services
{
    public class FullDecriptionExtractorService : IFullDecriptionExtractorService
    {
        private readonly IHttpHelperService _httpClient;
        private readonly ILogger<FullDecriptionExtractorService> _logger;

        public FullDecriptionExtractorService(ILogger<FullDecriptionExtractorService> logger, IHttpHelperService httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        public async Task<string> GetFullDescriptionAsync(string url, CancellationToken ct = default)
        {
            try
            {
                var html = await _httpClient.GetStringAsync(url, ct);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var host = new Uri(url).Host.ToLowerInvariant();
                var contentNode = SelectMainContentNode(doc, host) ?? doc.DocumentNode;

                CleanNode(contentNode, host);

                var hasVideo = contentNode.SelectSingleNode(".//video | .//iframe[contains(@src,'youtube')] | .//div[contains(@class,'video')]") != null;
                string rawText = hasVideo
                    ? contentNode.SelectSingleNode(".//p")?.InnerText ?? string.Empty
                    : contentNode.InnerText;

                var text = NormalizeText(rawText);

                text = host switch
                {
                    var h when h.Contains("tarafdari.com") => CleanTarafdariText(text),
                    var h when h.Contains("varzesh3.com") => CleanVarzesh3Text(text),
                    var h when h.Contains("khabarvarzeshi.com") => CleanKhabarVarzeshiText(text),
                    _ => text
                };

                return text.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract description from {Url}", url);
                return "[خطا در دریافت محتوا]";
            }
        }

        private HtmlNode SelectMainContentNode(HtmlDocument doc, string host)
        {
            return host switch
            {
                var h when h.Contains("varzesh3.com") => doc.DocumentNode.SelectSingleNode(
                    "//div[contains(@class,'news-body') or contains(@class,'news-text') or contains(@class,'newsPage')] | //article"),

                // طرفداری — پوشش کامل تمام ساختارها (شامل field-items و field-item)
                var h when h.Contains("tarafdari.com") => doc.DocumentNode.SelectSingleNode(
                    "//div[contains(@class,'node-content')] | " +
                    "//div[contains(@class,'field--name-body')] | " +
                    "//div[contains(@class,'field-items')] | " +
                    "//div[contains(@class,'field-item')] | " +
                    "//article"),

                var h when h.Contains("khabarvarzeshi.com") => SelectKhabarVarzeshiNode(doc),

                _ => doc.DocumentNode.SelectSingleNode(
                    "//article | " +
                    "//div[@itemprop='articleBody'] | " +
                    "//div[contains(@class,'news-content')] | " +
                    "//div[contains(@class,'body-content')] | " +
                    "//div[contains(@class,'content-body')] | " +
                    "//div[contains(@class,'post-body')] | " +
                    "//div[contains(@class,'entry-content')] | " +
                    "//div[contains(@class,'article-body')]"
                )
            };
        }

        private HtmlNode SelectKhabarVarzeshiNode(HtmlDocument doc)
        {
            var candidates = new[]
            {
                "//div[contains(@class,'news-content')]",
                "//div[contains(@class,'body-content')]",
                "//div[contains(@class,'content-body')]",
                "//div[contains(@class,'post-content')]",
                "//article//div[contains(@class,'content') or contains(@class,'body')]",
                "//div[@itemprop='articleBody']",
                "//article"
            };

            foreach (var xpath in candidates)
            {
                var node = doc.DocumentNode.SelectSingleNode(xpath);
                if (node != null && node.InnerText.Trim().Length > 150)
                    return node;
            }

            return doc.DocumentNode.SelectSingleNode("//article | //div[contains(@class,'content')]");
        }

        private void CleanNode(HtmlNode node, string host)
        {
            if (node == null) return;

            foreach (var tag in new[] { "script", "style", "nav", "header", "footer", "aside", "form", "noscript", "iframe", "svg" })
            {
                foreach (var n in node.SelectNodes($"//{tag}") ?? Enumerable.Empty<HtmlNode>())
                    n.Remove();
            }

            foreach (var xpath in new[]
            {
                "//*[contains(@class,'ad') or contains(@class,'banner') or contains(@class,'advert')]",
                "//*[contains(@id,'ad') or contains(@id,'banner')]",
                "//div[contains(@class,'share') or contains(@class,'social')]",
                "//div[contains(@class,'related') or contains(@class,'tag')]"
            })
            {
                foreach (var n in node.SelectNodes(xpath) ?? Enumerable.Empty<HtmlNode>())
                    n.Remove();
            }

            if (host.Contains("tarafdari.com"))
            {
                // حذف کامل عنوان خبر (h1)
                var titleNode = node.SelectSingleNode(".//h1[contains(@class,'node__title') or contains(@class,'title')]");
                titleNode?.Remove();

                // حذف منبع و آمار
                RemoveNodes(node, ".//*[contains(text(),'مشاهده')]");
                RemoveNodes(node, ".//*[contains(text(),'دیدگاه')]");
                RemoveNodes(node, ".//*[contains(text(),'طرفداری |')]");
                RemoveNodes(node, ".//div[contains(@class,'node__meta') or contains(@class,'author') or contains(@class,'submitted') or contains(@class,'meta') or contains(@class,'stats')]");
            }

            if (host.Contains("khabarvarzeshi.com"))
            {
                RemoveNodes(node, ".//div[contains(@class,'author') or contains(@class,'meta') or contains(@class,'time') or contains(@class,'share')]");
            }
        }

        private static void RemoveNodes(HtmlNode root, string xpath)
        {
            foreach (var node in root.SelectNodes(xpath) ?? Enumerable.Empty<HtmlNode>())
                node.Remove();
        }

        private string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            text = HtmlEntity.DeEntitize(text)
                             .Replace("\t", " ")
                             .Replace("\u200C", " ")
                             .Replace("\u200B", "")
                             .Replace(" ", " ")
                             .Trim();

            text = Regex.Replace(text, @"^\|+\s*", "");
            text = Regex.Replace(text, @"\s{2,}", " ");

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Trim())
                            .Where(l => l.Length > 3 && !l.StartsWith("خبرنگار") && !l.StartsWith("منبع"));

            return string.Join("\n\n", lines).Trim();
        }
     
        private string CleanTarafdariText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            // حذف منشن و هشتگ
            text = Regex.Replace(text, @"@\w+|#\w+", "");

            // حذف بخش "داغ‌ترین‌ها" و لیست خبرهای پیشنهادی (تا آخر متن)
            text = Regex.Replace(text, @"داغ\s*ترین\s*ها.*$", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // حذف تبلیغات
            text = Regex.Replace(text, @"با دعوت.*?هدیه بگیرید\.?", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"تبدیل\s*،?\s*\d+\s*هزار\s*تومان.*", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // حذف آمار مشاهده/دیدگاه
            text = Regex.Replace(text, @"\d+\s*(مشاهده|دیدگاه).*?$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

            // حذف فقط منبع "طرفداری |" — بدون حذف متن بعدش!
            text = Regex.Replace(text, @"^طرفداری\s*[\|｜:؛]\s*", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

            // حذف "به گزارش طرفداری"
            text = Regex.Replace(text, @"به\s*گزارش\s*طرفداری.*", "", RegexOptions.IgnoreCase);

            // حذف لینک‌ها
            text = Regex.Replace(text, @"https?://[^\s]+", "");

            // تمیز کردن فاصله‌ها و نیم‌فاصله
            text = Regex.Replace(text, @"[\u200B-\u200D\uFEFF\u2066-\u2069\u202A-\u202E]", "");
            text = Regex.Replace(text, @"(\u200C)+", "\u200C");
            text = Regex.Replace(text, @"[\u200C\s]{2,}", " ");

            return text.Trim();
        }

        private string CleanVarzesh3Text(string text)
        {
            text = Regex.Replace(text, @"کد\s*خبر.*", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"برای\s*مشاهده.*", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"ورود\s*\|\s*ثبت\s*نام.*", "", RegexOptions.Multiline);
            return text.Trim();
        }
        private static string CleanKhabarVarzeshiText(string text)
        {
            // ۱. حذف بخش "بیشتر بخوانید" و همه چیز بعدش (شایع در خبرورزشی)
            int moreIndex = text.IndexOf("بیشتر بخوانید:", StringComparison.OrdinalIgnoreCase);
            if (moreIndex >= 0)
                text = text.Substring(0, moreIndex).Trim();

            // ۲. حذف منشن و هشتگ
            text = Regex.Replace(text, @"@\w+|#\w+", " ", RegexOptions.IgnoreCase);

            // ۳. حذف آمار مشاهده/دیدگاه/نفر
            // ۳. حذف آمار مشاهده/دیدگاه/نفر — فقط خطوط کامل آمار (نه وسط جمله)
            text = Regex.Replace(text, @"^\s*\d+\s*(مشاهده|دیدگاه|نفر|بار|بازدید)[^\n]*$\n?", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

            // ۴. حذف بخش‌های منبع و نویسنده (بدون حذف متن اصلی)
            text = Regex.Replace(text, @"^خبرورزشی\s*[\|｜:؛]\s*", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            text = Regex.Replace(text, @"به\s*گزارش\s*خبرورزشی.*", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"^نویسنده\s*:\s*.*$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            text = Regex.Replace(text, @"^ارسال شده توسط\s*.*$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

            // ۵. حذف تبلیغات و لینک‌های پیشنهادی
            text = Regex.Replace(text, @"لینک کوتاه.*", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"کد خبر\s*:\s*\d+.*", "", RegexOptions.IgnoreCase);

            // ۶. حذف لینک‌ها
            text = Regex.Replace(text, @"https?://[^\s]+", " ", RegexOptions.Compiled);

            // ۷. حذف کاراکترهای مخفی و نامرئی
            text = Regex.Replace(text, @"[\u200B-\u200D\uFEFF\u2066-\u2069\u202A-\u202E]", "");

            // ۸. تمیز کردن نیم‌فاصله و فاصله‌های اضافی
            text = Regex.Replace(text, @"(\u200C)+", "\u200C"); // یک نیم‌فاصله نگه دار
            text = Regex.Replace(text, @"[\u200C\s]{2,}", " "); // فاصله‌های اضافی → یک فاصله

            // ۹. حذف خطوط خالی اضافی
            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Trim())
                            .Where(l => l.Length > 3)
                            .ToList();

            text = string.Join("\n\n", lines);
            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            return text.Trim();
        }
   
    }
}
 