using Microsoft.Playwright;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Services
{
    public class ImageExtractorService : IImageExtractorService
    {
        private readonly IPlaywrightService _playwright;
        private readonly ILogger<ImageExtractorService> _logger;

        public ImageExtractorService(ILogger<ImageExtractorService> logger, IPlaywrightService playwright)
        {
            _logger = logger;
            _playwright = playwright;
        }

        public async Task<string> ExtractMainImageAsync(string url, CancellationToken ct = default)
        {
            try
            {
                await using var context = await _playwright.CreateContextAsync(ct);
                var page = await context.NewPageAsync();
                try
                {
                    await page.GotoAsync(url, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 30000
                    });

                    var host = new Uri(url).Host.ToLowerInvariant();
                    var candidates = new List<ImageCandidate>();

                    await AddMetaImageAsync(page, url, "og:image", host.Contains("football360.ir") ? 2000 : 5000, candidates);
                    await AddMetaImageAsync(page, url, "og:image:secure_url", host.Contains("football360.ir") ? 2000 : 5000, candidates);
                    await AddMetaImageAsync(page, url, "twitter:image", host.Contains("football360.ir") ? 1500 : 4000, candidates);

                    if (host.Contains("football360.ir"))
                    {
                        string CleanUrl(string url)
                        {
                            if (string.IsNullOrWhiteSpace(url)) return null;
                            var clean = url.Split('?')[0];
                            clean = Regex.Replace(clean, @"[/_-](w|h|q)_?\d+[^/]*", "");
                            return clean;
                        }

                        var nextDataScript = await page.QuerySelectorAsync("script#__NEXT_DATA__");
                        if (nextDataScript == null) return null;

                        try
                        {
                            var jsonContent = await nextDataScript.TextContentAsync();
                            using var jsonDoc = JsonDocument.Parse(jsonContent ?? "{}");
                            var root = jsonDoc.RootElement;

                            if (!root.TryGetProperty("props", out var props) ||
                                !props.TryGetProperty("pageProps", out var pageProps) ||
                                !pageProps.TryGetProperty("data", out var data) ||
                                !data.TryGetProperty("post", out var post))
                                return null;

                            if (post.TryGetProperty("primary_media", out var primaryMedia) &&
                                primaryMedia.ValueKind == JsonValueKind.String)
                            {
                                var uri = primaryMedia.GetString();
                                if (!string.IsNullOrWhiteSpace(uri))
                                    return CleanUrl(uri);
                            }

                            if (post.TryGetProperty("medias", out var medias) &&
                                medias.ValueKind == JsonValueKind.Array &&
                                medias.GetArrayLength() > 0)
                            {
                                foreach (var media in medias.EnumerateArray())
                                {
                                    if (media.TryGetProperty("media", out var m) &&
                                        m.TryGetProperty("file", out var file) &&
                                        file.ValueKind == JsonValueKind.String)
                                    {
                                        var uri = file.GetString();
                                        if (!string.IsNullOrWhiteSpace(uri))
                                            return CleanUrl(uri);
                                    }
                                }
                            }

                            if (post.TryGetProperty("cover", out var cover) &&
                                cover.TryGetProperty("url", out var coverUrl) &&
                                coverUrl.ValueKind == JsonValueKind.String)
                            {
                                var uri = coverUrl.GetString();
                                if (!string.IsNullOrWhiteSpace(uri))
                                    return CleanUrl(uri);
                            }
                        }
                        catch
                        {
                        }

                        return null;
                    }

                    // Deserialize the JSON in .NET to avoid Playwright's converter for nested DTOs.
                    var imgDataJson = await page.EvaluateAsync<string>(@"
                        JSON.stringify(Array.from(document.querySelectorAll(
                            'article img, figure img, ' +
                            'div.news img, div.body img, div.content img, div.post img, div.article img, div.style_body img, ' +
                            'img.main, img.lead, img.hero, img.photo, img.image, img.thumb'
                        )).map(img => ({
                            srcset: img.getAttribute('srcset') || '',
                            src: img.getAttribute('src') || '',
                            dataSrc: img.getAttribute('data-src') || '',
                            dataLazySrc: img.getAttribute('data-lazy-src') || '',
                            dataOriginal: img.getAttribute('data-original') || '',
                            dataImage: img.getAttribute('data-image') || '',
                            imgClass: img.getAttribute('class') || '',
                            parentClass: img.parentElement?.getAttribute('class') || '',
                            parentTag: img.parentElement?.tagName?.toLowerCase() || '',
                            inFigure: !!img.closest('figure')
                        })))
                    ");
                    var imgDataList = JsonSerializer.Deserialize<List<ImgInfo>>(imgDataJson, JsonSerializerOptions.Web);

                    foreach (var imgData in imgDataList)
                    {
                        var src = imgData.BestSrc();
                        if (string.IsNullOrWhiteSpace(src)) continue;

                        var fullUrl = NormalizeUrl(url, src);
                        if (!IsValidMainImage(fullUrl, host)) continue;

                        var score = CalculateImageScore(fullUrl, imgData, host);
                        candidates.Add(new ImageCandidate(fullUrl, score));
                    }

                    if (!candidates.Any())
                    {
                        var fallbackSrc = await page.EvaluateAsync<string?>("document.querySelector('img')?.getAttribute('src')");
                        if (!string.IsNullOrWhiteSpace(fallbackSrc))
                        {
                            var fullUrl = NormalizeUrl(url, fallbackSrc);
                            if (IsValidMainImage(fullUrl, host))
                                candidates.Add(new ImageCandidate(fullUrl, 1000));
                        }
                    }

                    return candidates.OrderByDescending(c => c.Score).FirstOrDefault()?.Url;
                }
                finally
                {
                    await page.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "خطا در استخراج عکس از {Url}", url);
                return null;
            }
        }

        private record ImageCandidate(string Url, int Score);

        private class ImgInfo
        {
            public string Srcset { get; set; } = "";
            public string Src { get; set; } = "";
            public string DataSrc { get; set; } = "";
            public string DataLazySrc { get; set; } = "";
            public string DataOriginal { get; set; } = "";
            public string DataImage { get; set; } = "";
            public string ImgClass { get; set; } = "";
            public string ParentClass { get; set; } = "";
            public string ParentTag { get; set; } = "";
            public bool InFigure { get; set; }

            public string? BestSrc()
            {
                if (!string.IsNullOrWhiteSpace(Srcset))
                {
                    var best = Srcset.Split(',')
                        .Select(p => p.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        .Where(items => items.Length >= 2 && items[1].EndsWith("w"))
                        .OrderByDescending(items => int.TryParse(items[1][..^1], out var w) ? w : 0)
                        .FirstOrDefault();
                    if (best != null) return best[0];
                }

                return Src.NullIfEmpty()
                    ?? DataSrc.NullIfEmpty()
                    ?? DataLazySrc.NullIfEmpty()
                    ?? DataOriginal.NullIfEmpty()
                    ?? DataImage.NullIfEmpty();
            }
        }

        private async Task AddMetaImageAsync(IPage page, string baseUrl, string property, int score, List<ImageCandidate> list)
        {
            var value = await page.EvaluateAsync<string?>($@"
                document.querySelector('meta[property=""{property}""]')?.getAttribute('content') ||
                document.querySelector('meta[name=""{property}""]')?.getAttribute('content')
            ");

            if (string.IsNullOrWhiteSpace(value)) return;

            var normalized = NormalizeUrl(baseUrl, value.Trim());
            if (IsValidMainImage(normalized, new Uri(baseUrl).Host.ToLowerInvariant()))
                list.Add(new ImageCandidate(normalized, score));
        }

        private static int CalculateImageScore(string url, ImgInfo img, string host)
        {
            int score = 100;
            var lower = url.ToLowerInvariant();

            int width = 0;
            var m1 = Regex.Match(lower, @"(\d{3,})x(\d{3,})");
            if (m1.Success && int.TryParse(m1.Groups[1].Value, out var w1)) width = w1;

            var m2 = Regex.Match(lower, @"(\d{3,})w");
            if (m2.Success && int.TryParse(m2.Groups[1].Value, out var w2) && w2 > width) width = w2;

            if (width >= 1200) score += 30000;
            else if (width >= 1000) score += 25000;
            else if (width >= 800) score += 20000;
            else if (width >= 600) score += 15000;
            else if (width >= 400) score += 10000;

            if (img.InFigure || img.ParentTag == "figure") score += 40000;

            var classAttr = (img.ImgClass + " " + img.ParentClass).ToLowerInvariant();
            if (classAttr.Contains("main") || classAttr.Contains("lead") || classAttr.Contains("hero")
                || classAttr.Contains("photo") || classAttr.Contains("image") || classAttr.Contains("thumb"))
                score += 20000;

            if (IsBadImage(lower)) score -= 50000;
            if (lower.EndsWith(".gif")) score -= 30000;

            return Math.Max(score, 0);
        }

        private bool IsValidMainImage(string url, string host)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            var lower = url.ToLowerInvariant().Split('?')[0];
            if (!lower.EndsWith(".jpg") && !lower.EndsWith(".jpeg") && !lower.EndsWith(".png") && !lower.EndsWith(".webp")) return false;
            if (host.Contains("football360.ir") && lower.Contains("logo")) return false;
            return !IsBadImage(lower);
        }

        private static bool IsBadImage(string lower)
        {
            string[] bad =
            {
                "logo", "icon", "avatar", "banner", "advert", "ads", "ad-", "small", "tiny", "50x", "100x", "150x", "200x",
                "favicon", "profile", "placeholder", "watermark", "sponsor"
            };
            return bad.Any(k => lower.Contains(k));
        }

        private string NormalizeUrl(string baseUrl, string src)
        {
            if (string.IsNullOrWhiteSpace(src)) return src;
            if (src.StartsWith("http")) return src;
            if (src.StartsWith("//")) return "https:" + src;
            try
            {
                return new Uri(new Uri(baseUrl), src).ToString();
            }
            catch
            {
                return src;
            }
        }

        public async Task<bool> HasVideoAsync(string url, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (VideoExtensions.Any(ext => url.Contains(ext, StringComparison.OrdinalIgnoreCase)) ||
                EmbedPlatforms.Any(p => url.Contains(p, StringComparison.OrdinalIgnoreCase)))
                return true;

            try
            {
                await using var context = await _playwright.CreateContextAsync(ct);
                var page = await context.NewPageAsync();
                try
                {
                    await page.GotoAsync(url, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 30000
                    });

                    return await page.EvaluateAsync<bool>(@"() => {
                        if (document.querySelector('video[src], video source[src]')) return true;
                        const links = document.querySelectorAll('source[src], a[href*="".mp4""], a[href*="".m3u8""]');
                        for (const l of links) {
                            const s = (l.getAttribute('src') || '') + (l.getAttribute('href') || '');
                            if (s.match(/\.(mp4|webm|m3u8|mov|avi|flv|mkv|ts)/i)) return true;
                        }
                        const iframes = document.querySelectorAll('iframe');
                        for (const i of iframes) {
                            const s = i.getAttribute('src') || '';
                            const platforms = /youtube\.com|youtu\.be|aparat\.com|tamasha\.com|instagram\.com\/reel/;
                            const otherPlatforms = /tiktok\.com|vimeo\.com|varzesh3\.(?:com|ir)\/video|video\.varzesh3\.ir/;
                            if (platforms.test(s) || otherPlatforms.test(s)) return true;
                        }
                        return false;
                    }");
                }
                finally
                {
                    await page.CloseAsync();
                }
            }
            catch
            {
                return false;
            }
        }

        private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".m3u8", ".mov", ".avi", ".flv", ".mkv", ".ts" };
        private static readonly string[] EmbedPlatforms =
        {
            "youtube.com", "youtu.be", "aparat.com", "tamasha.com",
            "instagram.com/reel", "tiktok.com", "vimeo.com", "varzesh3.com/video",
            "varzesh3.ir/video", "video.varzesh3.ir"
        };
    }
}
