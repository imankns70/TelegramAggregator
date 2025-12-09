using HtmlAgilityPack;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Services
{
    public class Football360ScraperService : IFootball360ScraperService
    {
        private readonly IHttpHelperService _http;
        private readonly IDebugStore _debugStore;
        private const int MaxItems = 10; // افزایش دادم تا بیشتر بگیره

        public Football360ScraperService(IHttpHelperService http, IDebugStore debugStore)
        {
            _http = http;
            _debugStore = debugStore;
        }

        public async Task<List<NewsItem>> GetLatestNewsAsync(CancellationToken ct = default)
        {
            var newsList = new List<NewsItem>();

            try
            {
                var html = await _http.GetStringAsync("https://football360.ir/", ct);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // سلکتورهای جدید و گسترده — بر اساس ساختار واقعی سایت (دسامبر ۲۰۲۵)
                var newsNodes = doc.DocumentNode.SelectNodes(
                    "//li[contains(@class, 'post') or contains(@class, 'news-item') or contains(@class, 'card')] | " +
                    "//div[contains(@class, 'post') or contains(@class, 'news-item') or contains(@class, 'card') or contains(@class, 'item')] | " +
                    "//article | " +
                    "//section[contains(@class, 'posts') or contains(@class, 'news')]//li | " +
                    "//ul[contains(@class, 'posts') or contains(@class, 'news')]//li"  // لیست‌های خبر
                ) ?? Enumerable.Empty<HtmlNode>();



                foreach (var node in newsNodes.Take(MaxItems))
                {
                    try
                    {
                        var item = ExtractNewsItem(node);
                        if (item == null || string.IsNullOrWhiteSpace(item.Title))
                            continue;

                        await EnrichFullNewsAsync(item, ct);

                        _debugStore.LogOrUpdate(new DebugFeedItem
                        {
                            Link = item.Link,                    // لینک پاک شده                 
                            Title = item.Title,
                            SourceName = item.SourceName,
                            PubDate = item.PubDate.DateTime,
                            ProcessedAt = DateTime.UtcNow,
                            PublishStatus = "جدید_واکشی_شده"
                        });

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
                Console.WriteLine($"[Football360] خطا در دریافت صفحه اصلی: {ex.Message}");
            }

            return newsList;
        }

        private NewsItem ExtractNewsItem(HtmlNode node)
        {
            var item = new NewsItem
            {
                SourceName = "فوتبال ۳۶۰",
                PubDate = DateTimeOffset.UtcNow
            };

            // لینک خبر — سلکتورهای گسترده و fallback
            var linkNode = node.SelectSingleNode(".//a[contains(@href, '/news/') or contains(@href, '/post/') or contains(@href, '/article/')]");
            if (linkNode == null)
            {
                linkNode = node.SelectSingleNode(".//a[.//h3 or .//h2 or .//img or .//div[contains(@class,'title')]]");
            }
            if (linkNode == null)
            {
                linkNode = node.SelectSingleNode(".//a"); // آخرین fallback
            }
            if (linkNode == null) return null;

            var href = linkNode.GetAttributeValue("href", "").Trim();
            if (string.IsNullOrWhiteSpace(href)) return null;

            item.Link = href.StartsWith("http")
                ? href
                : "https://football360.ir" + (href.StartsWith("/") ? href : "/" + href);

            // عنوان — از لینک یا والد
            var titleNode = linkNode.SelectSingleNode(".//h3 | .//h2 | .//strong")
                          ?? node.SelectSingleNode(".//h3 | .//h2 | .//strong");
            item.Title = HttpUtility.HtmlDecode(titleNode?.InnerText.Trim() ?? linkNode.InnerText.Trim() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(item.Title)) return null;
            if (item.Title.Contains("لایو")) return null;


            item.Title = CleanDescription(item.Title);

          

            // شرح کوتاه — از p یا div.summary
            var leadNode = node.SelectSingleNode(".//p[contains(@class,'lead') or contains(@class,'excerpt') or contains(@class,'summary')] | .//div[contains(@class,'excerpt')] | .//p[not(preceding-sibling::h2)]");
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
                // Attribute ها — InnerText مقدار رو برمی‌گردونه
                node.SelectSingleNode(".//time/@datetime")?.InnerText,
                node.SelectSingleNode(".//meta[@property='article:published_time']/@content")?.InnerText,
                node.SelectSingleNode(".//meta[@name='date']/@content")?.InnerText,
                node.SelectSingleNode(".//meta[@itemprop='datePublished']/@content")?.InnerText,

                // تگ‌های معمولی
                node.SelectSingleNode(".//time")?.InnerText,
                node.SelectSingleNode(".//span[contains(@class,'date') or contains(@class,'time')]")?.InnerText,
                node.SelectSingleNode(".//div[contains(@class,'date')]")?.InnerText,
                node.SelectSingleNode(".//p[contains(@class,'date')]")?.InnerText
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

                var subTitleNode = doc.DocumentNode.SelectSingleNode("//div[@class='style_subTitle__iNvlW']");
                if (subTitleNode != null)
                {
                    item.ShortDescription = HttpUtility.HtmlDecode(subTitleNode.InnerText.Trim());
                    item.ShortDescription = CleanDescription(item.ShortDescription);

                }



                // شرح کامل — سلکتورهای گسترده و بهبودیافته
                var contentNode = doc.DocumentNode.SelectSingleNode("//div[@class='style_body__jQpYO']//div[@class='style_content__qroM7']");

                if (contentNode != null)
                {
                    // حذف عناصر غیرمتن (تبلیغات، اسکریپت و ...)
                    var junk = contentNode.SelectNodes(".//script | .//style | .//iframe | .//div[contains(@class,'ad')] | .//aside | .//footer | .//nav");
                    junk?.ToList().ForEach(n => n.Remove());

                    var fullText = contentNode.InnerText.Trim();
                    item.FullDescription = HttpUtility.HtmlDecode(fullText);
                }
                else
                {
                    item.FullDescription = item.ShortDescription ?? "توضیحی موجود نیست.";
                }

                item.FullDescription = CleanDescription(item.FullDescription);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Football360] خطا در تکمیل خبر {item.Link}: {ex.Message}");
                item.FullDescription ??= item.ShortDescription ?? "توضیحی موجود نیست.";
            }
        }

        private static string CleanDescription(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            // ۱. حذف همه کاراکترهای مخفی و نامرئی (Zero-Width و RTL/LTR marks)
            text = Regex.Replace(text, @"[\u200B-\u200D\uFEFF\u2066-\u2069\u202A-\u202E]", "", RegexOptions.Compiled);

            // ۲. حذف لینک‌ها
            text = Regex.Replace(text, @"https?://\S+", " ", RegexOptions.Compiled);

            // ۳. حذف عبارت‌های رایج منبع در فوتبال ۳۶۰
            text = Regex.Replace(text, @"به\s*گزارش\s*فوتبال\s*۳۶۰[،,:]?.*", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"فوتبال\s*۳۶۰\s*[\|｜:؛]\s*", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"^فوتبال\s*۳۶۰.*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);

            // ۴. حذف برچسب‌ها، تگ‌ها و بخش‌های پایانی (مثل "برچسب‌ها"، "تگ‌ها")
            text = Regex.Replace(text, @"برچسب[‌\s]*ها.*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"تگ[‌\s]*ها.*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"کلیدواژه[‌\s]*ها.*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);

            // ۵. حذف بخش "مشاهده" و "دیدگاه" و آمار
            text = Regex.Replace(text, @"\d+[\s\u200C]*مشاهده.*", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"\d+[\s\u200C]*دیدگاه.*", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"\d+[\s\u200C]*نفر.*", "", RegexOptions.Multiline);

            // ۶. حذف خطوط خالی و فاصله‌های اضافی
            text = Regex.Replace(text, @"\r\n|\r|\n", "\n", RegexOptions.Compiled); // یکسان‌سازی خطوط جدید
            text = Regex.Replace(text, @"^\s*$\n", "", RegexOptions.Multiline); // حذف خطوط خالی
            text = Regex.Replace(text, @"\n{3,}", "\n\n", RegexOptions.Compiled); // حداکثر دو خط خالی
            text = Regex.Replace(text, @"\s{2,}", " ", RegexOptions.Compiled); // حذف فاصله‌های اضافی

            // ۷. حذف کاراکترهای خاص و نیم‌فاصله‌های اشتباه
            text = text.Replace("\u200C", " "); // نیم‌فاصله رو به فاصله تبدیل کن (اگه اشتباه باشه)
            text = Regex.Replace(text, @"[ـ–—‒]", "-"); // همه خط تیره‌ها رو یکسان کن

            return text.Trim();
        }

    }
}