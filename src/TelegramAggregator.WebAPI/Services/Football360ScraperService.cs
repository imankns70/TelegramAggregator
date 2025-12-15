using HtmlAgilityPack;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using TelegramAggregator.WebAPI.Data.Repositories;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Services
{
    public class Football360ScraperService : IFootball360ScraperService
    {
        private readonly IHttpHelperService _http;
        private readonly IDebugStore _debugStore;
        private readonly INewsRepository _newsRepository;
        private const int MaxItems = 10; // افزایش دادم تا بیشتر بگیره

        public Football360ScraperService(IHttpHelperService http, IDebugStore debugStore, INewsRepository newsRepository)
        {
            _http = http;
            _debugStore = debugStore;
            _newsRepository = newsRepository;
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


                newsNodes = newsNodes.Take(MaxItems);
                foreach (var node in newsNodes)
                {
                    try
                    {
                        var item = ExtractNewsItem(node);
                        if (item == null || string.IsNullOrWhiteSpace(item.Title))
                            continue;

                        // اینجا باید از طریق لینک خبر در دیتابیس چک شود اگه وجود داشت، ادامه نده
                       
                        if (!_debugStore.GetAllDebugItems().Any(a => a.Link == item.Link))
                        {
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



            return item;
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
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // ۱. حذف بخش‌های خاص "بیشتر بخوانید" یا مشابه (اگر وجود داشت)
            int moreIndex = text.IndexOf("بیشتر بخوانید:", StringComparison.OrdinalIgnoreCase);
            if (moreIndex >= 0)
                text = text.Substring(0, moreIndex).Trim();

            // ۲. حذف منشن، هشتگ و لینک‌ها
            text = Regex.Replace(text, @"@\w+|#\w+|https?://[^\s]+", " ", RegexOptions.IgnoreCase);

            // ۳. حذف آمار مشاهده/دیدگاه/نفر/بار
            text = Regex.Replace(text, @"\d+\s*(مشاهده|دیدگاه|نفر|بار|بازدید).*", "", RegexOptions.IgnoreCase);

            // ۴. حذف منبع و گزارش فوتبال ۳۶۰ (بدون حذف متن اصلی)
            text = Regex.Replace(text, @"^فوتبال\s*۳۶۰\s*[\|｜:؛]\s*", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            text = Regex.Replace(text, @"به\s*گزارش\s*فوتبال\s*۳۶۰.*", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"^منبع\s*:\s*فوتبال\s*۳۶۰\s*$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

            // ۵. تبدیل «در گفت‌وگو با...» به «در گفت‌وگو با خبرنگار فوتبال فوری:»
            text = Regex.Replace(text,
                @"در\s*گفت[‌\u200C\s]*وگو\s*با\s*[:؛]?\s*",
                "در گفت‌وگو با خبرنگار فوتبال فوری: ",
                RegexOptions.IgnoreCase);

            // ۶. حذف برچسب‌ها و تگ‌ها
            text = Regex.Replace(text, @"(برچسب|تگ|کلیدواژه)[‌\s]*ها.*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);

            // ۷. حذف کاراکترهای مخفی و نامرئی (به جز نیم‌فاصله)
            text = Regex.Replace(text, @"[\u200B\u200D\uFEFF\u2066-\u2069\u202A-\u202E]", "");

            // ۸. تمیز کردن نیم‌فاصله و فاصله‌های اضافی
            text = Regex.Replace(text, @"(\u200C){2,}", "\u200C"); // نیم‌فاصله تکراری → یکی
            text = Regex.Replace(text, @"[\u200C\s]{2,}", " ");    // فاصله + نیم‌فاصله اضافی → یک فاصله

            // ۹. حذف خطوط خالی اضافی و ترکیب نهایی
            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Trim())
                            .Where(l => l.Length > 3)
                            .ToList();

            text = string.Join("\n\n", lines);
            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            // ۱۰. خط تیره‌ها رو یکسان کن
            text = Regex.Replace(text, @"[ـ–—‒]", "-");

            return text.Trim();
        }
    }
}