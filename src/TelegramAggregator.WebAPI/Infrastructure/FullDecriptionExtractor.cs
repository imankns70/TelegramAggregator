using HtmlAgilityPack;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TelegramAggregator.Infrastructure
{
    public class FullDecriptionExtractor : IFullDecriptionExtractor
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ImageExtractor> _logger;

        public FullDecriptionExtractor(IHttpClientFactory httpClientFactory, ILogger<ImageExtractor> logger)
        {
            _httpClient = httpClientFactory.CreateClient(nameof(FullDecriptionExtractor));
            _httpClient.Timeout = TimeSpan.FromSeconds(25);
            _logger = logger;
        }

        public async Task<string> GetFullDescriptionAsync(string url)
        {
            try
            {
                var html = await _httpClient.GetStringAsync(url);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                string host = new Uri(url).Host.ToLower();
                var contentNode = host switch
                {
                    var h when h.Contains("varzesh3.com") =>
                        doc.DocumentNode.SelectSingleNode("//div[contains(@class,'news-body') or contains(@class,'news-text') or contains(@class,'newsPage')]")
                        ?? doc.DocumentNode,

                    var h when h.Contains("tarafdari.com") =>
                        doc.DocumentNode.SelectSingleNode("//div[contains(@class,'node-content') or contains(@class,'field--name-body') or contains(@class,'field-name-body')]")
                        ?? doc.DocumentNode,

                    var h when h.Contains("khabarvarzeshi.com") =>
                        doc.DocumentNode.SelectSingleNode("//div[contains(@class,'story-body')]//div[contains(@class,'story')]")
                        ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'story-body')]")
                        ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'story')]")
                        ?? doc.DocumentNode,

                    _ => doc.DocumentNode.SelectSingleNode(
                        "//article" +
                        " | //div[contains(@class,'content')]" +
                        " | //div[contains(@class,'article-body')]" +
                        " | //div[contains(@class,'post-body')]" +
                        " | //div[contains(@class,'entry-content')]" +
                        " | //div[contains(@class,'news-body')]" +
                        " | //div[contains(@id,'main-content')]" +
                        " | //section[contains(@class,'main-content')]"
                    ) ?? doc.DocumentNode
                };

                CleanNode(contentNode, host);

                // بررسی وجود ویدیو در محتوا
                var videoNode = contentNode.SelectSingleNode(".//video | .//div[contains(@class,'video-container')]");
                string text;
                if (videoNode != null)
                {
                    // اگر ویدیو هست، فقط پاراگراف اول را بگیریم
                    var firstPara = contentNode.SelectSingleNode(".//p");
                    text = firstPara != null ? NormalizeText(firstPara.InnerText) : string.Empty;
                }
                else
                {
                    text = NormalizeText(contentNode.InnerText);
                }

                // حذف تبلیغات و اضافات سایت خاص
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
                return $"[Error extracting content] {ex.Message}";
            }
        }


        private string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = HtmlEntity.DeEntitize(text)
                             .Replace("\t", " ")
                             .Replace("\u200C", "")
                             .Replace("\u200B", "");

            text = Regex.Replace(text, @"^\|+\s*", ""); // حذف | اول متن
            text = Regex.Replace(text, @"\s{2,}", " ");

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Trim())
                            .Where(l => !string.IsNullOrEmpty(l) && l.Length > 2);

            return string.Join(Environment.NewLine + Environment.NewLine, lines).Trim();
        }

        private void CleanNode(HtmlNode node, string host)
        {
            if (node == null) return;

            foreach (var tag in new[] { "script", "style", "nav", "header", "footer", "aside", "form", "noscript", "iframe" })
                foreach (var n in node.SelectNodes($"//{tag}") ?? Enumerable.Empty<HtmlNode>())
                    n.Remove();

            foreach (var sel in new[] { "//*[contains(@class,'ad') or contains(@class,'banner')]", "//*[contains(@id,'ad')]" })
                foreach (var n in node.SelectNodes(sel) ?? Enumerable.Empty<HtmlNode>())
                    n.Remove();

            if (host.Contains("tarafdari.com"))
                RemoveNodes(node, ".//div[contains(@class,'node__meta') or contains(@class,'author') or contains(@class,'links') or contains(@class,'field--name-field-tags')]");
        }

        private static void RemoveNodes(HtmlNode root, string xpath)
        {
            foreach (var node in root.SelectNodes(xpath) ?? Enumerable.Empty<HtmlNode>())
                node.Remove();
        }

        private HtmlNode? SelectBestContentNode(HtmlDocument doc, string host) => host switch
        {
            var h when h.Contains("varzesh3.com") => doc.DocumentNode.SelectSingleNode("//div[contains(@class,'news-body')]") ?? doc.DocumentNode.SelectSingleNode("//article"),
            var h when h.Contains("tarafdari.com") => doc.DocumentNode.SelectSingleNode("//div[contains(@class,'node-content')]") ?? doc.DocumentNode.SelectSingleNode("//article"),
            var h when h.Contains("khabarvarzeshi") => doc.DocumentNode.SelectSingleNode("//div[contains(@class,'story-body')]") ?? doc.DocumentNode.SelectSingleNode("//article"),
            _ => doc.DocumentNode.SelectSingleNode("//article | //div[@itemprop='articleBody'] | //div[contains(@class,'content')]")
        };

        private string CleanTarafdariText(string text)
        {
            text = Regex.Replace(text, @"@\w+", "");
            text = Regex.Replace(text, @"#\w+", "");
            text = Regex.Replace(text, @"با دعوت.*?هدیه بگیرید\.?", "", RegexOptions.Singleline);
            text = Regex.Replace(text, @"تبدیل\s*،?\s*\d+\s*هزار\s*تومان.*", "", RegexOptions.Singleline);
            return text.Trim();
        }

        private string CleanVarzesh3Text(string text)
        {
            text = Regex.Replace(text, @"کد\s*خبر.*", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"برای\s*مشاهده.*", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"ورود\s*\|\s*ثبت\s*نام.*", "", RegexOptions.Multiline);
            return text.Trim();
        }

        private string CleanKhabarVarzeshiText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            // هر چیزی که قبل از اولین فعل مهم باشه → نویسنده‌ست!
            var patterns = new[]
            {
        @"^[\w\s\.\-–—|/:،]+?(?=\b(استقلال|پرسپولیس|دربی|طارمی|ساپینتو|محروم|اخراج|گل|پنالتی|داور|جنجال|قهرمان|صدر|لیگ|رسمی|فوری|اعلام|شکست|برد|تساوی|قهرمانی)\b)",
        @"^[\w\s\.\-–—|/:،]+?[:\-–—]\s*",
        @"^توسط\s+[\w\s\.]+",
        @"^نویسنده\s*[:\-]\s*[\w\s\.]+",
        @"^گزارش از\s+[\w\s\.]+"
    };

            foreach (var pattern in patterns)
                text = Regex.Replace(text, pattern, "", RegexOptions.IgnoreCase);

            // بقیه تمیزکاری‌ها
            text = Regex.Replace(text, @"خبر[:：]\s*\d+|زمان مطالعه[:：]\s*\d+\s*دقیقه|\|\s*", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\s{2,}", " ").Trim();

            return text;
        }

    }
}