using HtmlAgilityPack;
using System.Web;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Services
{
    public class Football360ScraperService : IFootball360ScraperService
    {
        private readonly IHttpHelperService _http;
        private const int MaxItems = 18;

        public Football360ScraperService(IHttpHelperService http)
        {
            _http = http;
        }

        public async Task<List<NewsItem>> GetLatestNewsAsync(CancellationToken ct = default)
        {
            var newsList = new List<NewsItem>();

            try
            {
                var html = await _http.GetStringAsync("https://football360.ir/", ct);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // سلکتورهای دقیق و تست‌شده برای football360.ir (دسامبر ۲۰۲۵
                var newsNodes = doc.DocumentNode.SelectNodes(
                    "//div[contains(@class,'news-card') or contains(@class,'post-card') or contains(@class,'news-item')] | " +
                    "//article[contains(@class,'post')] | " +
                    "//div[.//h3 or .//h2]//a[contains(@href,'/news/')]"
                ) ?? Enumerable.Empty<HtmlNode>();

            

                foreach (var node in newsNodes.Take(MaxItems))
                {
                    try
                    {
                        var item = ExtractNewsItem(node);
                        if (item == null || string.IsNullOrWhiteSpace(item.Title))
                            continue;

                        await EnrichFullNewsAsync(item, ct);

                     
                        newsList.Add(item);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Football360] خطا در پردازش یک خبر: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Football360] خطا در دریافت صفحه اصلی");
            }

            return newsList
                .Where(n => !string.IsNullOrWhiteSpace(n.Title))
                .OrderByDescending(n => n.PubDate)
                .ToList();
        }

        private NewsItem? ExtractNewsItem(HtmlNode node)
        {
            var item = new NewsItem
            {
                SourceName = "فوتبال ۳۶۰",
                PubDate = DateTimeOffset.UtcNow
            };

            // لینک خبر
            var linkNode = node.SelectSingleNode(".//a[contains(@href, '/news/') or contains(@href, '/post/')]");
            if (linkNode == null) return null;

            var href = linkNode.GetAttributeValue("href", "").Trim();
            if (string.IsNullOrWhiteSpace(href)) return null;

            item.Link = href.StartsWith("http")
                ? href
                : "https://football360.ir" + (href.StartsWith("/") ? href : "/" + href);

            // عنوان
            var titleNode = node.SelectSingleNode(".//h3 | .//h2 | .//div[contains(@class,'title')]//strong | .//a//strong");
            item.Title = HttpUtility.HtmlDecode(titleNode?.InnerText.Trim() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(item.Title)) return null;

            // عکس
            var imgNode = node.SelectSingleNode(".//img");
            if (imgNode != null)
            {
                var src = imgNode.GetAttributeValue("data-src", "")
                       ?? imgNode.GetAttributeValue("data-lazy-src", "")
                       ?? imgNode.GetAttributeValue("src", "");

                if (!string.IsNullOrWhiteSpace(src))
                {
                    item.ImageUrl = src.StartsWith("http")
                        ? src
                        : "https://football360.ir" + (src.StartsWith("/") ? src : "/" + src);
                }
            }

            // شرح کوتاه
            var leadNode = node.SelectSingleNode(".//p | .//div[contains(@class,'lead') or contains(@class,'excerpt')]");
            item.ShortDescription = HttpUtility.HtmlDecode(leadNode?.InnerText.Trim() ?? "");

           
            // نوع محتوا (ویدیو)
            item.Type = node.SelectSingleNode(".//iframe | .//video") != null ? "Video" : "News";

            // زمان موقت از کارت
            TryExtractPubDateFromCard(node, item);

            return item;
        }

        private static void TryExtractPubDateFromCard(HtmlNode node, NewsItem item)
        {
            var candidates = new[]
            {
                // Attribute ها (InnerText در HtmlAgilityPack مقدار attribute رو برمی‌گردونه!)
                node.SelectSingleNode(".//time/@datetime")?.InnerText,
                node.SelectSingleNode(".//meta[@property='article:published_time']/@content")?.InnerText,
                node.SelectSingleNode(".//meta[@name='date']/@content")?.InnerText,
                node.SelectSingleNode(".//meta[@itemprop='datePublished']/@content")?.InnerText,

                // تگ‌های معمولی
                node.SelectSingleNode(".//time")?.InnerText,
                node.SelectSingleNode(".//span[contains(@class,'date') or contains(@class,'time')]")?.InnerText,
                node.SelectSingleNode(".//div[contains(@class,'date')]")?.InnerText
            };

            foreach (var text in candidates)
            {
                if (!string.IsNullOrWhiteSpace(text) && DateTimeOffset.TryParse(text.Trim(), out var dt))
                {
                    item.PubDate = dt;
                    return;
                }
            }
        }

        private async Task EnrichFullNewsAsync(NewsItem item, CancellationToken ct)
        {
            try
            {
                var html = await _http.GetStringAsync(item.Link, ct);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // زمان دقیق — با روش امن (بدون OwnerNode و بدون as HtmlAttribute)
                var timeCandidates = new[]
                {
                    doc.DocumentNode.SelectSingleNode("//meta[@property='article:published_time']/@content")?.InnerText,
                    doc.DocumentNode.SelectSingleNode("//meta[@name='date']/@content")?.InnerText,
                    doc.DocumentNode.SelectSingleNode("//time/@datetime")?.InnerText,
                    doc.DocumentNode.SelectSingleNode("//time")?.InnerText,
                    doc.DocumentNode.SelectSingleNode("//span[contains(@class,'date')]")?.InnerText
                };

                foreach (var t in timeCandidates)
                {
                    if (!string.IsNullOrWhiteSpace(t) && DateTimeOffset.TryParse(t.Trim(), out var dt))
                    {
                        item.PubDate = dt;
                        break;
                    }
                }

                // شرح کامل
                var contentNode = doc.DocumentNode.SelectSingleNode(
                    "//div[contains(@class,'news-content') or contains(@class,'content') or contains(@class,'post-body')] | " +
                    "//article | //div[@id='content'] | //div[contains(@class,'body')]"
                );

                if (contentNode != null)
                {
                    // حذف تبلیغات و اسکریپت
                    var junk = contentNode.SelectNodes(".//script | .//style | .//iframe | .//div[contains(@class,'ad')] | .//aside");
                    junk?.ToList().ForEach(n => n.Remove());

                    item.FullDescription = HttpUtility.HtmlDecode(contentNode.InnerText.Trim());
                }
                else
                {
                    item.FullDescription = item.ShortDescription ?? "توضیحی موجود نیست.";
                }

                // ویدیو یوتیوب — روش امن و تضمینی
                var iframe = doc.DocumentNode.SelectSingleNode("//iframe[contains(@src,'youtube') or contains(@src,'youtu.be')]");
                item.VideoUrl = iframe?.Attributes["src"]?.Value?.Trim();

            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Football360] خطا در تکمیل خبر {item.Link}: {ex.Message}");
                item.FullDescription ??= item.ShortDescription ?? "توضیحی موجود نیست.";
            }
        }
    }
}