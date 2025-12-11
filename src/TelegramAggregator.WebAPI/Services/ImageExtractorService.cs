using HtmlAgilityPack;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Services
{
    public class ImageExtractorService : IImageExtractorService
    {
        private readonly IHttpHelperService _httpClient;
        private readonly ILogger<ImageExtractorService> _logger;

        public ImageExtractorService(ILogger<ImageExtractorService> logger, IHttpHelperService httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        public async Task<string> ExtractMainImageAsync(string url, CancellationToken ct = default)
        {
            try
            {
                var html = await _httpClient.GetStringAsync(url, ct);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var host = new Uri(url).Host.ToLowerInvariant();
                var candidates = new List<ImageCandidate>();

                // ۱. متا تگ‌ها — فقط به عنوان کاندید (اولویت پایین برای football360)
                AddMetaImage(doc, url, "og:image", host.Contains("football360.ir") ? 2000 : 5000, candidates);
                AddMetaImage(doc, url, "og:image:secure_url", host.Contains("football360.ir") ? 2000 : 5000, candidates);
                AddMetaImage(doc, url, "twitter:image", host.Contains("football360.ir") ? 1500 : 4000, candidates);

                // ۲. سلکتورهای ویژه فوتبال ۳۶۰ — اولویت بالا



                if (host.Contains("football360.ir"))
                {
                    string CleanUrl(string url)
                    {
                        if (string.IsNullOrWhiteSpace(url)) return null;
                        var clean = url.Split('?')[0];
                        clean = Regex.Replace(clean, @"[/_-](w|h|q)_?\d+[^/]*", "");
                        return clean;
                    }

                    var nextDataScript = doc.DocumentNode.SelectSingleNode("//script[@id='__NEXT_DATA__']");
                    if (nextDataScript == null) return null;

                    try
                    {
                        using var jsonDoc = JsonDocument.Parse(nextDataScript.InnerText);
                        var root = jsonDoc.RootElement;

                        if (!root.TryGetProperty("props", out var props) ||
                            !props.TryGetProperty("pageProps", out var pageProps) ||
                            !pageProps.TryGetProperty("data", out var data) ||
                            !data.TryGetProperty("post", out var post))
                            return null;

                        // 1. primary_media (اولویت اول)
                        if (post.TryGetProperty("primary_media", out var primaryMedia) &&
                            primaryMedia.ValueKind == JsonValueKind.String)
                        {
                            var uri = primaryMedia.GetString();
                            if (!string.IsNullOrWhiteSpace(uri))
                                return CleanUrl(uri);
                        }

                        // 2. medias[] → اولین فایل
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

                        // 3. cover.url
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
                        // اگر JSON خراب بود، بی‌خیال
                    }

                    return null;
                }


                // ۳. تصاویر عمومی داخل محتوا — برای همه سایت‌ها
                var generalImgs = doc.DocumentNode.SelectNodes(
                "//article//img | //figure//img | " +
                "//div[contains(@class,'news') or contains(@class,'body') or contains(@class,'content') or contains(@class,'post') or contains(@class,'article') or contains(@class,'style_body')]//img | " +
                "//img[contains(@class,'main') or contains(@class,'lead') or contains(@class,'hero') or contains(@class,'photo') or contains(@class,'image') or contains(@class,'thumb')]"
            );

                if (generalImgs != null)
                {
                    foreach (var img in generalImgs)
                    {
                        var src = ExtractBestImageUrl(img);
                        if (string.IsNullOrWhiteSpace(src)) continue;

                        var fullUrl = NormalizeUrl(url, src);
                        if (!IsValidMainImage(fullUrl, host)) continue;

                        var score = CalculateImageScore(fullUrl, img, host);
                        candidates.Add(new ImageCandidate(fullUrl, score));
                    }
                }

                // ۴. fallback: اولین img بزرگ
                if (!candidates.Any())
                {
                    var fallback = doc.DocumentNode.SelectSingleNode("//img");
                    if (fallback != null)
                    {
                        var src = ExtractBestImageUrl(fallback);
                        if (!string.IsNullOrWhiteSpace(src))
                        {
                            var fullUrl = NormalizeUrl(url, src);
                            if (IsValidMainImage(fullUrl, host))
                                candidates.Add(new ImageCandidate(fullUrl, 1000));
                        }
                    }
                }

                return candidates.OrderByDescending(c => c.Score).FirstOrDefault()?.Url;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "خطا در استخراج عکس از {Url}", url);
                return null;
            }
        }

        private record ImageCandidate(string Url, int Score);

        private void AddMetaImage(HtmlDocument doc, string baseUrl, string property, int score, List<ImageCandidate> list)
        {
            var value = doc.DocumentNode
                .SelectSingleNode($"//meta[@property='{property}'] | //meta[@name='{property}']")
                ?.GetAttributeValue("content", null);

            if (string.IsNullOrWhiteSpace(value)) return;

            var normalized = NormalizeUrl(baseUrl, value.Trim());
            if (IsValidMainImage(normalized, new Uri(baseUrl).Host.ToLowerInvariant()))
                list.Add(new ImageCandidate(normalized, score));
        }

        private string? ExtractBestImageUrl(HtmlNode img)
        {
            var srcset = img.GetAttributeValue("srcset", null);
            if (!string.IsNullOrWhiteSpace(srcset))
            {
                var best = srcset.Split(',')
                    .Select(p => p.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    .Where(items => items.Length >= 2 && items[1].EndsWith("w"))
                    .OrderByDescending(items => int.TryParse(items[1][..^1], out var w) ? w : 0)
                    .FirstOrDefault();

                if (best != null) return best[0];
            }

            return img.GetAttributeValue("src", null)
                   ?? img.GetAttributeValue("data-src", null)
                   ?? img.GetAttributeValue("data-lazy-src", null)
                   ?? img.GetAttributeValue("data-original", null)
                   ?? img.GetAttributeValue("data-image", null);
        }

        private int CalculateImageScore(string url, HtmlNode img, string host)
        {
            int score = 100;
            var lower = url.ToLowerInvariant();

            // تشخیص عرض از URL
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

            // داخل figure یا کلاس‌های خاص
            if (img.Ancestors("figure").Any() || img.ParentNode.Name == "figure") score += 40000;
            var classAttr = (img.GetAttributeValue("class", "") + " " + img.ParentNode?.GetAttributeValue("class", "")).ToLowerInvariant();
            if (classAttr.Contains("main") || classAttr.Contains("lead") || classAttr.Contains("hero") || classAttr.Contains("photo") || classAttr.Contains("image") || classAttr.Contains("thumb"))
                score += 20000;

            // جریمه لوگو و تبلیغ
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

        private bool IsBadImage(string lower)
        {
            string[] bad = { "logo", "icon", "avatar", "banner", "advert", "ads", "ad-", "small", "tiny", "50x", "100x", "150x", "200x", "favicon", "profile", "placeholder", "watermark", "sponsor" };
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

        // HasVideoAsync — بدون تغییر (درست کار می‌کنه)
        public async Task<bool> HasVideoAsync(string url, CancellationToken ct = default)
        {
            // کد قبلی بدون تغییر
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (VideoExtensions.Any(ext => url.Contains(ext, StringComparison.OrdinalIgnoreCase)) ||
                EmbedPlatforms.Any(p => url.Contains(p, StringComparison.OrdinalIgnoreCase)))
                return true;

            try
            {
                var htmlResult = await _httpClient.TryHtmlForCheckVideoAsync(url, ct);
                if (!htmlResult.Item1) return false;
                if (string.IsNullOrEmpty(htmlResult.Item2)) return true;

                var doc = new HtmlDocument();
                doc.LoadHtml(htmlResult.Item2);

                if (doc.DocumentNode.SelectSingleNode("//video[@src] | //video/source[@src]") != null) return true;

                var links = doc.DocumentNode.SelectNodes("//source[@src] | //a[contains(@href, '.mp4') or contains(@href, '.m3u8')]");
                if (links?.Any(l => VideoExtensions.Any(ext => (l.GetAttributeValue("src", "") + l.GetAttributeValue("href", "")).Contains(ext, StringComparison.OrdinalIgnoreCase))) == true)
                    return true;

                var iframes = doc.DocumentNode.SelectNodes("//iframe");
                if (iframes?.Any(i => EmbedPlatforms.Any(p => i.GetAttributeValue("src", "").Contains(p, StringComparison.OrdinalIgnoreCase))) == true)
                    return true;

                return false;
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
            "instagram.com/reel", "tiktok.com", "vimeo.com", "varzesh3.com/video"
        };
    }
}

//using AngleSharp;
//using AngleSharp.Dom;
//using HtmlAgilityPack;
//using System.Text.Json;
//using System.Text.RegularExpressions;

//namespace TelegramAggregator.WebAPI.Services
//{
//    public class ImageExtractorService : IImageExtractorService
//    {
//        private readonly IHttpHelperService _httpClient;
//        private readonly ILogger<ImageExtractorService> _logger;

//        public ImageExtractorService(ILogger<ImageExtractorService> logger, IHttpHelperService httpClient)
//        {
//            _logger = logger;
//            _httpClient = httpClient;
//        }


//        public async Task<string?> ExtractMainImageAsync(string url, CancellationToken ct = default)
//        {
//            try
//            {
//                var html = await _httpClient.GetStringAsync(url, ct);
//                var doc = new HtmlDocument();
//                doc.LoadHtml(html);
//                var host = new Uri(url).Host.ToLowerInvariant();
//                var candidates = new List<ImageCandidate>();
//                // ۱. متا تگ‌ها (og:image, twitter:image) — فقط کاندید
//                AddMetaImage(doc, url, "og:image", 4000, candidates);
//                AddMetaImage(doc, url, "og:image:secure_url", 4000, candidates);
//                AddMetaImage(doc, url, "twitter:image", 3000, candidates);
//                // ۲. تصاویر داخل محتوا — اولویت اصلی
//                var imgNodes = doc.DocumentNode.SelectNodes(
//                    "//article//img | //figure//img | " +
//                    "//div[contains(@class,'news') or contains(@class,'body') or contains(@class,'content') or contains(@class,'post')]//img | " +
//                    "//img[contains(@class,'main') or contains(@class,'lead') or contains(@class,'hero') or contains(@class,'photo') or contains(@class,'image')]"
//                );
//                if (imgNodes != null)
//                {
//                    foreach (var img in imgNodes)
//                    {
//                        var src = ExtractBestImageUrl(img);
//                        if (string.IsNullOrWhiteSpace(src)) continue;
//                        var fullUrl = NormalizeUrl(url, src);
//                        if (!IsValidMainImage(fullUrl)) continue;
//                        var score = CalculateImageScore(fullUrl, img, host);
//                        candidates.Add(new ImageCandidate(fullUrl, score));
//                    }
//                }
//                return candidates.OrderByDescending(c => c.Score).FirstOrDefault()?.Url;
//            }
//            catch (Exception ex)
//            {
//                _logger.LogWarning(ex, "خطا در استخراج عکس از {Url}", url);
//                return null;
//            }
//        }

//        private record ImageCandidate(string Url, int Score);
//        private void AddMetaImage(HtmlDocument doc, string baseUrl, string property, int score, List<ImageCandidate> list)
//        {
//            var value = doc.DocumentNode
//                .SelectSingleNode($"//meta[@property='{property}'] | //meta[@name='{property}']")
//                ?.GetAttributeValue("content", null);
//            if (string.IsNullOrWhiteSpace(value)) return;
//            var host = new Uri(baseUrl).Host.ToLowerInvariant();
//            var normalized = NormalizeUrl(baseUrl, value.Trim());
//            // خبرورزشی: اگه og:image لوگو بود → ردش کن!
//            if ((host.Contains("khabarvarzeshi.com") || host.Contains("khabarvarzeshi.ir"))
//                && (normalized.Contains("logo") || normalized.Contains("favicon") || normalized.Contains("default")))
//                return;
//            if (IsValidMainImage(normalized))
//                list.Add(new ImageCandidate(normalized, score));
//        }
//        private string? ExtractBestImageUrl(HtmlNode img)
//        {
//            // اول srcset رو چک کن — بهترین منبع رو بر اساس عرض (w) انتخاب می‌کنه
//            var srcset = img.GetAttributeValue("srcset", null);
//            if (!string.IsNullOrWhiteSpace(srcset))
//            {
//                var candidates = srcset.Split(',')
//                    .Select(part =>
//                    {
//                        var items = part.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
//                        var url = items[0];
//                        var descriptor = items.Length > 1 ? items[1] : "";
//                        var width = 0;
//                        if (descriptor.EndsWith("w") && int.TryParse(descriptor[..^1], out var w))
//                            width = w;
//                        else if (Regex.Match(url, @"(\d{3,})w").Success)
//                        {
//                            var m = Regex.Match(url, @"(\d{3,})w");
//                            if (int.TryParse(m.Groups[1].Value, out var mw)) width = mw;
//                        }
//                        return new { Url = url, Width = width };
//                    })
//                    .Where(x => x.Width > 0)
//                    .OrderByDescending(x => x.Width)
//                    .FirstOrDefault();
//                if (candidates != null)
//                    return candidates.Url;
//            }
//            // اگه srcset نبود یا خالی بود → src معمولی
//            return img.GetAttributeValue("src", null)
//                   ?? img.GetAttributeValue("data-src", null)
//                   ?? img.GetAttributeValue("data-lazy-src", null)
//                   ?? img.GetAttributeValue("data-original", null);
//        }
//        private int ExtractWidthFromUrl(string url)
//        {
//            var m = Regex.Match(url.ToLowerInvariant(), @"(\d{3,})w");
//            return m.Success && int.TryParse(m.Groups[1].Value, out var w) ? w : 0;
//        }
//        private int CalculateImageScore(string url, HtmlNode img, string host)
//        {
//            int score = 100;
//            var lower = url.ToLowerInvariant();
//            // تشخیص عرض از URL — هم xxxx x xxxx و هم xxxxw
//            int detectedWidth = 0;
//            var sizeMatch = Regex.Match(lower, @"(\d{3,})x(\d{3,})");
//            if (sizeMatch.Success && int.TryParse(sizeMatch.Groups[1].Value, out var w1))
//                detectedWidth = w1;
//            var wMatch = Regex.Match(lower, @"(\d{3,})w");
//            if (wMatch.Success && int.TryParse(wMatch.Groups[1].Value, out var w2) && w2 > detectedWidth)
//                detectedWidth = w2;
//            // امتیاز بر اساس عرض واقعی
//            if (detectedWidth >= 1200) score += 20000;
//            else if (detectedWidth >= 1000) score += 16000;
//            else if (detectedWidth >= 800) score += 12000;
//            else if (detectedWidth >= 600) score += 8000;
//            else if (detectedWidth >= 400) score += 4000;
//            // داخل figure = عکس اصلی خبر (خبرورزشی همیشه اینجوری می‌کنه!)
//            if (img.ParentNode.Name == "figure" || img.Ancestors("figure").Any())
//                score += 25000; // بالاترین امتیاز ممکن
//                                // کلاس‌های خبرورزشی
//            var classId = (img.GetAttributeValue("class", "") + " " + img.GetAttributeValue("id", "")).ToLowerInvariant();
//            if (classId.Contains("main") || classId.Contains("photo") || classId.Contains("image") || classId.Contains("lead"))
//                score += 15000;
//            // جریمه سنگین برای og:image در خبرورزشی (همیشه لوگوئه!)
//            if (host.Contains("khabarvarzeshi.com") || host.Contains("khabarvarzeshi.ir"))
//            {
//                if (img.Name == "meta" || lower.Contains("logo") || lower.Contains("default") || lower.Contains("favicon"))
//                    return 1; // کاملاً حذفش کن!
//            }
//            // جریمه سنگین برای گیف (۹۹٪ تبلیغ یا لوگو متحرک در خبرورزشی!)
//            if (lower.EndsWith(".gif"))
//            {
//                // اگه از متا تگ اومده → ۱۰۰٪ تبلیغ
//                if (img.Name == "meta") return 0;
//                // اگه از محتوا اومده ولی داخل figure نباشه → احتمالاً تبلیغ
//                if (!img.Ancestors("figure").Any())
//                    score -= 40000;
//                // در کل گیف‌ها امتیاز خیلی کم می‌گیرن
//                score -= 20000;
//            }
//            // جریمه لوگو و تبلیغ
//            if (IsBadImage(lower))
//                score -= 50000;
//            return Math.Max(score, 0);
//        }
//        private bool IsValidMainImage(string url)
//        {
//            if (string.IsNullOrWhiteSpace(url)) return false;
//            var lower = url.ToLowerInvariant();
//            // حذف کوئری استرینگ قبل از بررسی پسوند
//            var cleanUrl = lower.Split('?')[0];
//            // بررسی پسوند واقعی فایل
//            bool hasValidExtension = cleanUrl.EndsWith(".jpg") ||
//                                     cleanUrl.EndsWith(".jpeg") ||
//                                     cleanUrl.EndsWith(".png") ||
//                                     cleanUrl.EndsWith(".webp");
//            if (!hasValidExtension) return false;
//            // حالا بررسی کنیم که لوگو یا تبلیغ نباشه
//            return !IsBadImage(lower);
//        }
//        private bool IsBadImage(string lower)
//        {
//            string[] bad = { "logo", "icon", "avatar", "banner", "advert", "ads", "ad-", "thumb", "small", "tiny", "50x", "100x", "150x", "200x", "favicon", "profile", "placeholder", "watermark", "sponsor" };
//            return bad.Any(k => lower.Contains(k));
//        }




//        private string NormalizeUrl(string baseUrl, string src)
//        {
//            if (string.IsNullOrWhiteSpace(src)) return src;
//            if (src.StartsWith("http")) return src;
//            if (src.StartsWith("//")) return "https:" + src;
//            return new Uri(new Uri(baseUrl), src).ToString();
//        }

//        // HasVideoAsync — سریع، هوشمند و بدون مرورگر

//        public async Task<bool> HasVideoAsync(string url, CancellationToken ct = default)
//        {
//            if (string.IsNullOrWhiteSpace(url)) return false;

//            // مرحله ۰: تشخیص فوری از روی URL
//            if (VideoExtensions.Any(ext => url.Contains(ext, StringComparison.OrdinalIgnoreCase)) ||
//                EmbedPlatforms.Any(p => url.Contains(p, StringComparison.OrdinalIgnoreCase)))
//                return true;

//            try
//            {

//                (bool, string) httpResult = await _httpClient.TryHtmlForCheckVideoAsync(url, ct);

//                if (httpResult.Item1 == false)
//                    return false;

//                if (httpResult.Item1 && string.IsNullOrEmpty(httpResult.Item2))
//                    return true;


//                var doc = new HtmlAgilityPack.HtmlDocument();
//                doc.LoadHtml(httpResult.Item2);
//                var root = doc.DocumentNode;

//                // ۱. تگ <video> واقعی
//                if (root.SelectSingleNode("//video[source] | //video[@src]") != null)
//                    return true;

//                // ۲. لینک مستقیم به فایل ویدئویی
//                var videoLinks = root.SelectNodes("//source[@src] | //a[contains(@href, '.mp4') or contains(@href, '.m3u8')]");
//                if (videoLinks?.Any(n =>
//                {
//                    var src = n.GetAttributeValue("src", "") + n.GetAttributeValue("href", "");
//                    return VideoExtensions.Any(ext => src.Contains(ext, StringComparison.OrdinalIgnoreCase));
//                }) == true)
//                    return true;

//                // ۳. iframe واقعی از پلتفرم‌های ویدیویی
//                var iframes = root.SelectNodes("//iframe[contains(@src,'embed') or contains(@src,'watch') or contains(@src,'/v/')]");
//                if (iframes?.Any(iframe =>
//                {
//                    var src = iframe.GetAttributeValue("src", "");
//                    return EmbedPlatforms.Any(p => src.Contains(p, StringComparison.OrdinalIgnoreCase));
//                }) == true)
//                    return true;

//                return false;
//            }
//            catch
//            {
//                return false; // هر خطایی → فرض بر نبود ویدئو (امن‌ترین حالت)
//            }
//        }
//        private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".m3u8", ".mov", ".avi", ".flv", ".mkv", ".ts" };
//        private static readonly string[] EmbedPlatforms =
//        {
//            "youtube.com", "youtu.be", "aparat.com", "tamasha.com",
//            "instagram.com/reel", "tiktok.com", "vimeo.com", "varzesh3.com/video"
//        };
//    }



//}
