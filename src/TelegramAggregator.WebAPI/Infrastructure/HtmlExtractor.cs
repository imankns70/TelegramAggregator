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

                CleanNode(contentNode);

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
                    _ => text
                };

                return text.Trim();
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

                string host = new Uri(url).Host.ToLower();
                HtmlNode? imgNode = host switch
                {
                    var h when h.Contains("varzesh3.com") => doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']"),
                    var h when h.Contains("tarafdari.com") => doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:image']"),
                    _ => doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")
                };

                if (imgNode == null)
                {
                    imgNode = doc.DocumentNode.SelectSingleNode(
                        "//article//img | //div[contains(@class,'content')]//img | //div[contains(@class,'article-body')]//img"
                    );
                }

                if (imgNode != null)
                {
                    var imgUrl = imgNode.GetAttributeValue("content", null) ?? imgNode.GetAttributeValue("src", null);
                    if (!string.IsNullOrWhiteSpace(imgUrl))
                    {
                        // نادیده گرفتن عکس ویدیو تلگرام یا آیکون‌ها
                        if (!imgUrl.Contains("telegram") && !imgUrl.Contains("video"))
                            return NormalizeUrl(url, imgUrl);
                    }
                }
            }
            catch { }

            return null;
        }

        private void CleanNode(HtmlNode node)
        {
            var tagsToRemove = new[] { "script", "style", "nav", "header", "footer", "form", "aside" };
            foreach (var tag in tagsToRemove)
            {
                var nodes = node.SelectNodes($"//{tag}");
                if (nodes == null) continue;
                foreach (var n in nodes) n.Remove();
            }

            var adNodes = node.SelectNodes("//*[contains(text(),'اپلیکیشن') or contains(text(),'پخش زنده') or contains(text(),'پادکست')]");
            if (adNodes != null)
                foreach (var ad in adNodes) ad.Remove();
        }

        private string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = HtmlEntity.DeEntitize(text)
                             .Replace("\t", " ")
                             .Replace("\u200C", "")
                             .Replace("\u200B", "");

            // حذف کاراکتر | ابتدای متن و فاصله‌های اضافی بعدش
            text = Regex.Replace(text, @"^\|+\s*", "");

            text = Regex.Replace(text, @"\s{2,}", " ");

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Trim())
                            .Where(l => !string.IsNullOrEmpty(l) && l.Length > 2);

            return string.Join(Environment.NewLine + Environment.NewLine, lines).Trim();
        }


        private string CleanTarafdariText(string text)
        {
            text = Regex.Replace(text, @"@\w+", ""); // حذف @username
            text = Regex.Replace(text, @"#\w+", ""); // حذف هشتگ‌ها
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

        private string NormalizeUrl(string baseUrl, string src)
        {
            if (Uri.TryCreate(src, UriKind.Absolute, out var absoluteUri))
                return absoluteUri.ToString();

            if (!baseUrl.EndsWith("/"))
                baseUrl += "/";

            if (Uri.TryCreate(new Uri(baseUrl), src, out var relativeUri))
                return relativeUri.ToString();

            return src;
        }
    }
}
