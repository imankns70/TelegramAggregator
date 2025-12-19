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
        private readonly INewsRepository _newsRepository;
        private const int MaxItems = 10; // افزایش دادم تا بیشتر بگیره

        public Football360ScraperService(IHttpHelperService http, INewsRepository newsRepository)
        {
            _http = http; 
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

                        newsList.Add(item);
                    }


                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Football360] خطا در پردازش یک خبر: {ex.Message}");
                    }
                }


                
                
                _newsRepository.FilterNews(newsList);
                foreach (var item in newsList)
                {
                    await EnrichFullNewsAsync(item, ct);
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
                SourceName = "فوتبال 360",
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
                var html = await _http.GetStringAsync("https://football360.ir/post/20251215151/3-%D9%87%D9%81%D8%AA%D9%87-3-%D8%A7%D9%85%D8%AA%DB%8C%D8%A7%D8%B2-%D8%A8%D8%B1%D8%A7%DB%8C-%D8%A7%D8%B3%D8%AA%D9%82%D9%84%D8%A7%D9%84-%D8%AE%D9%86%D8%AF%D9%87%D9%87%D8%A7%DB%8C-%D9%87%DB%8C%D8%B3%D8%AA%D8%B1%DB%8C%DA%A9-%D8%B1%DB%8C%DA%A9%D8%A7%D8%B1%D8%AF%D9%88", ct);
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

            // ۱. حذف کاراکترهای مخفی و نامرئی (به جز نیم‌فاصله!)
            text = Regex.Replace(text, @"[\u200B\u200D\uFEFF\u2066-\u2069\u202A-\u202E]", "");

            // ۲-الف. اگر «به گزارش» به جمله قبلی چسبیده، جداش کن
            text = Regex.Replace(
                text,
                @"([\.!\؟])(?=به\s*گزارش)",
                "$1 "
            );

            // ۲. تبدیل "به گزارش فوتبال 360 / ۳۶۰" → "به گزارش فوتبال فوری" (در هر جای متن)
            text = Regex.Replace(
                text,
                @"به\s*گزارش\s*فوتبال\s*(?:360|۳۶۰)",
                "به گزارش فوتبال فوری",
                RegexOptions.IgnoreCase
            );



            // ۳. حذف کامل باقی‌مانده‌های «فوتبال ۳۶۰» (برای موارد خاص)
            text = Regex.Replace(text, @"«?\s*فوتبال\s*[۳3]۶۰\s*»?", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"فوتبال\s*360", "", RegexOptions.IgnoreCase);

            // ۴. تبدیل «در گفت‌وگو با...» → «در گفت‌وگو با خبرنگار فوتبال فوری:»
            text = Regex.Replace(text,
                @"در\s*گفت[‌\u200C\s]*وگو\s*با\s*[:؛]?\s*",
                "در گفت‌وگو با خبرنگار فوتبال فوری: ",
                RegexOptions.IgnoreCase);

            // ۵. حذف خطوطی که فقط منبع هستن
            text = Regex.Replace(text, @"^(منبع|گزارش|نویسنده)?\s*[:：؛]?\s*فوتبال\s*[۳3]۶۰\s*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);

            // ۶. حذف لینک‌ها، منشن و هشتگ
            text = Regex.Replace(text, @"https?://[^\s]+|@\w+|#\w+", " ", RegexOptions.IgnoreCase);

            // ۷. حذف برچسب‌ها، آمار مشاهده/دیدگاه
            text = Regex.Replace(text, @"(برچسب|تگ|کلیدواژه)[‌\s]*ها.*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\d+[\s\u200C]*(مشاهده|دیدگاه|نفر|بار|بازدید).*", "", RegexOptions.IgnoreCase);

            // ۸. حذف تکرار خطوط کامل
            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Trim())
                            .Where(l => l.Length > 3)
                            .ToList();

            var uniqueLines = new List<string>();
            string lastLine = string.Empty;
            foreach (var line in lines)
            {
                if (line != lastLine)
                {
                    uniqueLines.Add(line);
                    lastLine = line;
                }
            }

            text = string.Join("\n\n", uniqueLines);

            // ۹. تمیزکاری نهایی — نیم‌فاصله‌ها حفظ می‌شن!
            text = Regex.Replace(text, @"[\s]{2,}", " ");
            text = Regex.Replace(text, @"(\u200C){2,}", "\u200C");
            text = Regex.Replace(text, @"[ـ–—‒]", "-");
            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            // ۱۰. حذف گیومه‌های خالی یا علامت‌های اضافی
            text = Regex.Replace(text, @"\s*«\s*»\s*", " ");
            text = Regex.Replace(text, @"\s*[:؛]\s*$", "");

            return text.Trim();
        }
    }
}