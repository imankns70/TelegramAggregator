using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace TelegramAggregator.WebAPI.Services
{
    public class FullDecriptionExtractorService : IFullDecriptionExtractorService
    {
        private readonly IHttpHelperService _httpClient;
        private readonly ILogger<FullDecriptionExtractorService> _logger; // بهتره از همین کلاس لاگ بگیریم

        public FullDecriptionExtractorService(ILogger<FullDecriptionExtractorService> logger, IHttpHelperService httpClient)
        {   
        
            _logger = logger;
            _httpClient = httpClient;
        }

        public async Task<string> GetFullDescriptionAsync(string url)
        {
            try
            {
                var html = await _httpClient.GetStringAsync(url);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var host = new Uri(url).Host.ToLowerInvariant();
                var contentNode = SelectMainContentNode(doc, host) ?? doc.DocumentNode;

                CleanNode(contentNode, host);

                // اگر ویدیو باشه فقط پاراگراف اول رو برگردون
                var hasVideo = contentNode.SelectSingleNode(".//video | .//iframe[contains(@src,'youtube')] | .//div[contains(@class,'video')]") != null;
                string rawText = hasVideo
                    ? contentNode.SelectSingleNode(".//p")?.InnerText ?? string.Empty
                    : contentNode.InnerText;

                var text = NormalizeText(rawText);

                // تمیزکاری اختصاصی هر سایت
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
                return $"[خطا در دریافت محتوا]";
            }
        }

        private HtmlNode? SelectMainContentNode(HtmlDocument doc, string host)
        {
            return host switch
            {
                // ورزش سه - خیلی پایدار
                var h when h.Contains("varzesh3.com") => doc.DocumentNode.SelectSingleNode(
                    "//div[contains(@class,'news-body') or contains(@class,'news-text') or contains(@class,'newsPage')] | //article"),

                // طرفداری - پایدار
                var h when h.Contains("tarafdari.com") => doc.DocumentNode.SelectSingleNode(
                    "//div[contains(@class,'node-content') or contains(@class,'field--name-body')] | //article"),

                // خبرورزشی - ساختار جدید ۱۴۰۴
                var h when h.Contains("khabarvarzeshi.com") => SelectKhabarVarzeshiNode(doc),

                // بقیه سایت‌ها - عمومی اما هوشمند
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

        private HtmlNode? SelectKhabarVarzeshiNode(HtmlDocument doc)
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

            // حذف تگ‌های غیرضروری
            foreach (var tag in new[] { "script", "style", "nav", "header", "footer", "aside", "form", "noscript", "iframe", "svg" })
            {
                foreach (var n in node.SelectNodes($"//{tag}") ?? Enumerable.Empty<HtmlNode>())
                    n.Remove();
            }

            // حذف تبلیغات
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
                // حذف کامل عنوان خبر (h1) از داخل محتوا — این خط طلاییه!
                var titleNode = node.SelectSingleNode(".//h1[contains(@class,'node__title') or contains(@class,'title')]");
                titleNode?.Remove();

                // حذف کامل خط کثیف "عنوان + مشاهده + دیدگاه + عدد"
                RemoveNodes(node, ".//*[contains(text(),'مشاهده') and contains(text(),'دیدگاه')]");
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
                             .Replace(" ", " ") // فضای غیرقابل شکستن
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
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // ۱. حذف منشن‌ها و هشتگ‌ها
            text = Regex.Replace(text, @"@\w+", " ", RegexOptions.Compiled);
            text = Regex.Replace(text, @"#\w+", " ", RegexOptions.Compiled);

            // ۲. حذف بخش "داغ‌ترین‌ها" و لیست خبرهای پیشنهادی (شامل عنوان و خبرها)
            text = Regex.Replace(text, @"داغ\s*ترین\s*ها.*$", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // ۳. حذف تبلیغات دعوت به هدیه، تبدیل پول و چیزهای مشابه
            text = Regex.Replace(text, @"با دعوت.*?هدیه بگیرید\.?", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"تبدیل\s*،?\s*\d+\s*هزار\s*تومان.*", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // ۴. حذف "مشاهده / دیدگاه" و آمار
            text = Regex.Replace(text, @"\d+\s*(مشاهده|دیدگاه).*?$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

            // ۵. حذف منبع و عنوان‌های اضافی طرفداری
            text = Regex.Replace(text, @"^طرفداری\s*[\|｜:؛]\s*.*", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            text = Regex.Replace(text, @"^طرفداری\s*$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            text = Regex.Replace(text, @"به\s*گزارش\s*طرفداری.*", "", RegexOptions.IgnoreCase);

            // ۶. حذف لینک‌ها
            text = Regex.Replace(text, @"https?://[^\s]+", " ", RegexOptions.Compiled);

            // ۷. حذف کاراکترهای مخفی و نیم‌فاصله‌های اشتباه
            text = Regex.Replace(text, @"[\u200B-\u200D\uFEFF\u2066-\u2069\u202A-\u202E]", "", RegexOptions.Compiled);
            text = text.Replace("\u200C", " "); // نیم‌فاصله رو به فاصله تبدیل کن

            // ۸. حذف فاصله‌ها و خطوط اضافی
            text = Regex.Replace(text, @"\s{2,}", " ", RegexOptions.Compiled);
            text = Regex.Replace(text, @"^\s+|\s+$", "", RegexOptions.Multiline); // trim هر خط
            text = Regex.Replace(text, @"\n{3,}", "\n\n"); // حداکثر دو خط خالی

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

            // پیدا کردن موقعیت عبارت "وی افزود متن کامل گفتگو را اینجا بخوانید." و حذف متن از آن به بعد
            int moreReadIndex = text.IndexOf("وی افزود متن کامل گفتگو را اینجا بخوانید.");
            if (moreReadIndex >= 0)
            {
                text = text.Substring(0, moreReadIndex).Trim(); // فقط بخش قبل از "وی افزود متن کامل گفتگو را اینجا بخوانید." را نگه می‌دارد
            }

            var lines = text.Split('\n')
                            .Select(l => l.Trim())
                            .Where(l => l.Length > 0)
                            .ToList();

            var result = new List<string>();
            bool contentStarted = false;

            foreach (var line in lines)
            {
                // رد کردن خطوط نویسنده، منبع، زمان مطالعه
                if (!contentStarted)
                {
                    if (Regex.IsMatch(line, @"(نویسنده|خبرنگار|منبع|زمان مطالعه|ارسال شده توسط|گزارش از)", RegexOptions.IgnoreCase))
                        continue;

                    if (line.Length < 80 && Regex.IsMatch(line, @"^[آ-ی\s]+\s*[:\-–—]"))
                        continue;

                    if (line.Contains("خبر :") || line.Contains("زمان مطالعه :"))
                        continue;

                    contentStarted = true;
                }

                // حذف باقی‌مانده‌های اضافی
                var cleaned = Regex.Replace(line, @"\|.*$|خبر\s*:\s*\d+|زمان مطالعه.*|#\w+|@\w+", "", RegexOptions.IgnoreCase);
                if (cleaned.Length > 3)
                    result.Add(cleaned.Trim());
            }

            var final = string.Join("\n\n", result);
            final = Regex.Replace(final, @"\n{3,}", "\n\n");
            return final.Trim();
        }

    }
}