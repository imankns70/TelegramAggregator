using Microsoft.Playwright;
using System.Net;
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
        private readonly IPlaywrightService _playwright;
        private readonly INewsRepository _newsRepository;
        private const int MaxItems = 10;

        public Football360ScraperService(IPlaywrightService playwright, INewsRepository newsRepository)
        {
            _playwright = playwright;
            _newsRepository = newsRepository;
        }

        public async Task<List<NewsItem>> GetLatestNewsAsync(CancellationToken ct = default)
        {
            var newsList = new List<NewsItem>();

            try
            {
                await using var context = await _playwright.CreateContextAsync(ct);
                var page = await context.NewPageAsync();
                try
                {
                    await page.GotoAsync("https://football360.ir/", new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 30000
                    });

                    var newsNodes = await page.QuerySelectorAllAsync("div[class*='NewNews_postItem'] a[href*='/post/']");
                    if (newsNodes.Count == 0)
                    {
                        newsNodes = await page.QuerySelectorAllAsync("article a[href*='/post/']");
                    }

                    if (newsNodes.Count == 0)
                    {
                        Console.WriteLine("[Football360] هیچ خبری پیدا نشد – سایت ممکنه تغییر کرده باشه.");
                        return newsList;
                    }

                    var hrefs = new List<string>();
                    var seen = new HashSet<string>();
                    foreach (var node in newsNodes)
                    {
                        var href = (await node.GetAttributeAsync("href") ?? "").Trim();
                        if (!string.IsNullOrEmpty(href) && seen.Add(href))
                            hrefs.Add(href);
                        if (hrefs.Count >= MaxItems) break;
                    }

                    var tasks = hrefs.Select(href => ExtractNewsItem(context, href, ct));
                    var results = await Task.WhenAll(tasks);

                    foreach (var newsItem in results)
                    {
                        if (newsItem != null &&
                            !string.IsNullOrWhiteSpace(newsItem.Title) &&
                            !string.IsNullOrWhiteSpace(newsItem.ShortDescription))
                        {
                            newsList.Add(newsItem);
                        }
                    }
                }
                finally
                {
                    await page.CloseAsync();
                }

                _newsRepository.FilterNews(newsList);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Football360] خطا در دریافت صفحه اصلی: {ex.Message}");
            }

            return newsList;
        }

        private async Task<NewsItem> ExtractNewsItem(IBrowserContext context, string href, CancellationToken ct)
        {
            var item = new NewsItem
            {
                SourceName = "فوتبال 360",
                PubDate = DateTimeOffset.UtcNow
            };

            if (string.IsNullOrWhiteSpace(href)) return null;

            item.Link = "https://football360.ir" + (href.StartsWith("/") ? href : "/" + href);

            var page = await context.NewPageAsync();
            try
            {
                await page.GotoAsync(item.Link, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30000
                });

                var titleNode = await page.QuerySelectorAsync("h3");
                string title = titleNode != null
                    ? (await titleNode.TextContentAsync())?.Trim() ?? ""
                    : "";

                if (string.IsNullOrWhiteSpace(title) || title.Length < 10) return null;

                item.Title = CleanDescription(HttpUtility.HtmlDecode(title));
                if (item.Title.Contains("لایو") || item.Title.Contains("زنده") || item.Title.Contains("فانتزی")) return null;

                var subtitleNode = await page.QuerySelectorAsync("div[class*='style_subTitle__iNvlW']");
                if (subtitleNode != null)
                {
                    item.ShortDescription = CleanDescription(
                        HttpUtility.HtmlDecode((await subtitleNode.TextContentAsync())?.Trim() ?? "")
                    );
                }

                var paragraphNodes = await page.QuerySelectorAllAsync("div[class*='style_content__qroM7'] p");
                if (paragraphNodes.Count > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var p in paragraphNodes)
                    {
                        var text = NormalizeText(await p.TextContentAsync() ?? "");
                        if (text.Length < 20) continue;
                        sb.AppendLine(text);
                        sb.AppendLine();
                    }

                    item.FullDescription = CleanDescription(
                        HttpUtility.HtmlDecode(sb.ToString().Trim()));
                }
            }
            finally
            {
                await page.CloseAsync();
            }

            return item;
        }

        private async Task EnrichFullNewsAsync(IBrowserContext context, NewsItem item, CancellationToken ct)
        {
            try
            {
                var page = await context.NewPageAsync();
                try
                {
                    await page.GotoAsync(item.Link, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 30000
                    });

                    var subTitleNode = await page.QuerySelectorAsync("div.style_subTitle__iNvlW");
                    if (subTitleNode != null)
                    {
                        item.ShortDescription = HttpUtility.HtmlDecode((await subTitleNode.TextContentAsync())?.Trim() ?? "");
                        item.ShortDescription = CleanDescription(item.ShortDescription);
                    }

                    var fullText = await page.EvaluateAsync<string?>(@"
                        const content = document.querySelector('div.style_body__jQpYO div.style_content__qroM7');
                        if (!content) return null;
                        content.querySelectorAll('script, style, iframe, [class*=""ad""], aside, footer, nav').forEach(e => e.remove());
                        return content.textContent;
                    ");

                    if (fullText != null)
                    {
                        item.FullDescription = HttpUtility.HtmlDecode(fullText.Trim());
                    }
                    else
                    {
                        item.FullDescription = item.ShortDescription ?? "توضیحی موجود نیست.";
                    }

                    item.FullDescription = CleanDescription(item.FullDescription);
                }
                finally
                {
                    await page.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Football360] خطا در تکمیل خبر {item.Link}: {ex.Message}");
                item.FullDescription ??= item.ShortDescription ?? "توضیحی موجود نیست.";
            }
        }

        private string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            text = WebUtility.HtmlDecode(text)
                             .Replace("\t", " ")
                             .Replace("\u200C", " ")
                             .Replace("\u200B", "")
                             .Replace(" ", " ")
                             .Trim();

            text = Regex.Replace(text, @"^\|+\s*", "");
            text = Regex.Replace(text, @"\s{2,}", " ");

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Trim())
                            .Where(l => l.Length > 3 && !l.StartsWith("خبرنگار") && !l.StartsWith("منبع"));

            return string.Join("\n\n", lines).Trim();
        }

        private static string CleanDescription(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = Regex.Replace(text, @"[\u200B\u200D\uFEFF\u2066-\u2069\u202A-\u202E]", "");

            text = Regex.Replace(
                text,
                @"([\.!\؟])(?=به\s*گزارش)",
                "$1 "
            );

            text = Regex.Replace(
                text,
                @"به\s*گزارش\s*فوتبال\s*(?:360|۳۶۰)",
                "به گزارش فوتبال فوری",
                RegexOptions.IgnoreCase
            );

            text = Regex.Replace(text, @"«?\s*فوتبال\s*[۳3]۶۰\s*»?", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"فوتبال\s*360", "", RegexOptions.IgnoreCase);

            text = Regex.Replace(text,
                @"در\s*گفت[‌\u200C\s]*وگو\s*با\s*[:؛]?\s*",
                "در گفت‌وگو با خبرنگار فوتبال فوری: ",
                RegexOptions.IgnoreCase);

            text = Regex.Replace(text, @"^(منبع|گزارش|نویسنده)?\s*[:：؛]?\s*فوتبال\s*[۳3]۶۰\s*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);

            text = Regex.Replace(text, @"https?://[^\s]+|@\w+|#\w+", " ", RegexOptions.IgnoreCase);

            text = Regex.Replace(text, @"(برچسب|تگ|کلیدواژه)[‌\s]*ها.*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\d+[\s\u200C]*(مشاهده|دیدگاه|نفر|بار|بازدید).*", "", RegexOptions.IgnoreCase);

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

            text = Regex.Replace(text, @"[\s]{2,}", " ");
            text = Regex.Replace(text, @"(\u200C){2,}", "\u200C");
            text = Regex.Replace(text, @"[ـ–—‒]", "-");
            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            text = Regex.Replace(text, @"\s*«\s*»\s*", " ");
            text = Regex.Replace(text, @"\s*[:؛]\s*$", "");

            return text.Trim();
        }
    }
}
