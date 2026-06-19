using Microsoft.Playwright;
using System.Net;
using System.Text.RegularExpressions;

namespace TelegramAggregator.WebAPI.Services
{
    public class FullDecriptionExtractorService : IFullDecriptionExtractorService
    {
        private readonly IPlaywrightService _playwright;
        private readonly ILogger<FullDecriptionExtractorService> _logger;

        public FullDecriptionExtractorService(ILogger<FullDecriptionExtractorService> logger, IPlaywrightService playwright)
        {
            _logger = logger;
            _playwright = playwright;
        }

        public async Task<string> GetFullDescriptionAsync(string url)
        {
            try
            {
                await using var context = await _playwright.CreateContextAsync();
                var page = await context.NewPageAsync();
                try
                {
                    await page.GotoAsync(url, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 30000
                    });

                    var host = new Uri(url).Host.ToLowerInvariant();

                    await CleanPageAsync(page, host);

                    string selector;
                    if (host.Contains("khabarvarzeshi.com"))
                        selector = await GetKhabarVarzeshiSelector(page);
                    else
                        selector = GetContentSelector(host);

                    var rawText = await page.EvaluateAsync<string?>(@"
                        const el = document.querySelector(arguments[0]);
                        if (!el) return null;
                        const hasVideo = el.querySelector('video, iframe[src*=""youtube""], div[class*=""video""]');
                        if (hasVideo) {
                            const p = el.querySelector('p');
                            return p ? p.textContent : '';
                        }
                        return el.textContent;
                    ", selector);

                    var text = NormalizeText(rawText ?? "");

                    text = host switch
                    {
                        var h when h.Contains("tarafdari.com") => CleanTarafdariText(text),
                        var h when h.Contains("varzesh3.com") => CleanVarzesh3Text(text),
                        var h when h.Contains("khabarvarzeshi.com") => CleanKhabarVarzeshiText(text),
                        _ => text
                    };

                    return text.Trim();
                }
                finally
                {
                    await page.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract description from {Url}", url);
                return "[خطا در دریافت محتوا]";
            }
        }

        private string GetContentSelector(string host)
        {
            return host switch
            {
                var h when h.Contains("varzesh3.com") => "div.news-body, div.news-text, div.newsPage, article",

                var h when h.Contains("tarafdari.com") =>
                    "div.node-content, div.field--name-body, div.field-items, div.field-item, article",

                _ =>
                    "article, [itemprop='articleBody'], div.news-content, div.body-content, " +
                    "div.content-body, div.post-body, div.entry-content, div.article-body"
            };
        }

        private async Task<string> GetKhabarVarzeshiSelector(IPage page)
        {
            var candidates = new[]
            {
                "div.news-content",
                "div.body-content",
                "div.content-body",
                "div.post-content",
                "article div.content, article div.body",
                "[itemprop='articleBody']",
                "article"
            };

            foreach (var sel in candidates)
            {
                var node = await page.QuerySelectorAsync(sel);
                if (node != null)
                {
                    var text = await node.TextContentAsync();
                    if (text?.Trim().Length > 150) return sel;
                }
            }

            return "article, div.content";
        }

        private async Task CleanPageAsync(IPage page, string host)
        {
            await page.EvaluateAsync(@"
                const tags = ['script', 'style', 'nav', 'header', 'footer', 'aside', 'form', 'noscript', 'iframe', 'svg'];
                tags.forEach(tag => document.querySelectorAll(tag).forEach(e => e.remove()));

                document.querySelectorAll('[class*=""ad""], [class*=""banner""], [class*=""advert""]').forEach(e => e.remove());
                document.querySelectorAll('[id*=""ad""], [id*=""banner""]').forEach(e => e.remove());
                document.querySelectorAll('div[class*=""share""], div[class*=""social""]').forEach(e => e.remove());
                document.querySelectorAll('div[class*=""related""], div[class*=""tag""]').forEach(e => e.remove());
            ");

            if (host.Contains("tarafdari.com"))
            {
                await page.EvaluateAsync(@"
                    const title = document.querySelector('h1.node__title, h1.title');
                    if (title) title.remove();

                    document.querySelectorAll('div[class*=""node__meta""], div[class*=""author""], div[class*=""submitted""], div[class*=""meta""], div[class*=""stats""]').forEach(e => e.remove());

                    document.querySelectorAll('*').forEach(el => {
                        const t = el.textContent?.trim() || '';
                        if (t.length < 100 && (t.includes('مشاهده') || t.includes('دیدگاه') || t.includes('طرفداری |'))) {
                            el.remove();
                        }
                    });
                ");
            }

            if (host.Contains("khabarvarzeshi.com"))
            {
                await page.EvaluateAsync(@"
                    document.querySelectorAll('div[class*=""author""], div[class*=""meta""], div[class*=""time""], div[class*=""share""]').forEach(e => e.remove());
                ");
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

        private string CleanTarafdariText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            text = Regex.Replace(text, @"@\w+|#\w+", "");
            text = Regex.Replace(text, @"داغ\s*ترین\s*ها.*$", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"با دعوت.*?هدیه بگیرید\.?", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"تبدیل\s*،?\s*\d+\s*هزار\s*تومان.*", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"\d+\s*(مشاهده|دیدگاه).*?$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            text = Regex.Replace(text, @"^طرفداری\s*[\|｜:؛]\s*", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            text = Regex.Replace(text, @"به\s*گزارش\s*طرفداری.*", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"https?://[^\s]+", "");
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
            int moreIndex = text.IndexOf("بیشتر بخوانید:", StringComparison.OrdinalIgnoreCase);
            if (moreIndex >= 0)
                text = text.Substring(0, moreIndex).Trim();

            text = Regex.Replace(text, @"@\w+|#\w+", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"^\s*\d+\s*(مشاهده|دیدگاه|نفر|بار|بازدید)[^\n]*$\n?", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            text = Regex.Replace(text, @"^خبرورزشی\s*[\|｜:؛]\s*", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            text = Regex.Replace(text, @"به\s*گزارش\s*خبرورزشی.*", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"^نویسنده\s*:\s*.*$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            text = Regex.Replace(text, @"^ارسال شده توسط\s*.*$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            text = Regex.Replace(text, @"لینک کوتاه.*", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"کد خبر\s*:\s*\d+.*", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"https?://[^\s]+", " ", RegexOptions.Compiled);
            text = Regex.Replace(text, @"[\u200B-\u200D\uFEFF\u2066-\u2069\u202A-\u202E]", "");
            text = Regex.Replace(text, @"(\u200C)+", "\u200C");
            text = Regex.Replace(text, @"[\u200C\s]{2,}", " ");

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
