using AngleSharp;
using AngleSharp.Dom;
using HtmlAgilityPack;
using System.Text.Json;
using System.Text.RegularExpressions;

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


        public async Task<string?> ExtractMainImageAsync(string url, CancellationToken ct = default)
        {
            try
            {
                var html = await _httpClient.GetStringAsync(url, ct);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                var host = new Uri(url).Host.ToLowerInvariant();
                var candidates = new List<ImageCandidate>();
                // ۱. متا تگ‌ها (og:image, twitter:image) — فقط کاندید
                AddMetaImage(doc, url, "og:image", 4000, candidates);
                AddMetaImage(doc, url, "og:image:secure_url", 4000, candidates);
                AddMetaImage(doc, url, "twitter:image", 3000, candidates);
                // ۲. تصاویر داخل محتوا — اولویت اصلی
                var imgNodes = doc.DocumentNode.SelectNodes(
                    "//article//img | //figure//img | " +
                    "//div[contains(@class,'news') or contains(@class,'body') or contains(@class,'content') or contains(@class,'post')]//img | " +
                    "//img[contains(@class,'main') or contains(@class,'lead') or contains(@class,'hero') or contains(@class,'photo') or contains(@class,'image')]"
                );
                if (imgNodes != null)
                {
                    foreach (var img in imgNodes)
                    {
                        var src = ExtractBestImageUrl(img);
                        if (string.IsNullOrWhiteSpace(src)) continue;
                        var fullUrl = NormalizeUrl(url, src);
                        if (!IsValidMainImage(fullUrl)) continue;
                        var score = CalculateImageScore(fullUrl, img, host);
                        candidates.Add(new ImageCandidate(fullUrl, score));
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
            var host = new Uri(baseUrl).Host.ToLowerInvariant();
            var normalized = NormalizeUrl(baseUrl, value.Trim());
            // خبرورزشی: اگه og:image لوگو بود → ردش کن!
            if ((host.Contains("khabarvarzeshi.com") || host.Contains("khabarvarzeshi.ir"))
                && (normalized.Contains("logo") || normalized.Contains("favicon") || normalized.Contains("default")))
                return;
            if (IsValidMainImage(normalized))
                list.Add(new ImageCandidate(normalized, score));
        }
        private string? ExtractBestImageUrl(HtmlNode img)
        {
            // اول srcset رو چک کن — بهترین منبع رو بر اساس عرض (w) انتخاب می‌کنه
            var srcset = img.GetAttributeValue("srcset", null);
            if (!string.IsNullOrWhiteSpace(srcset))
            {
                var candidates = srcset.Split(',')
                    .Select(part =>
                    {
                        var items = part.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var url = items[0];
                        var descriptor = items.Length > 1 ? items[1] : "";
                        var width = 0;
                        if (descriptor.EndsWith("w") && int.TryParse(descriptor[..^1], out var w))
                            width = w;
                        else if (Regex.Match(url, @"(\d{3,})w").Success)
                        {
                            var m = Regex.Match(url, @"(\d{3,})w");
                            if (int.TryParse(m.Groups[1].Value, out var mw)) width = mw;
                        }
                        return new { Url = url, Width = width };
                    })
                    .Where(x => x.Width > 0)
                    .OrderByDescending(x => x.Width)
                    .FirstOrDefault();
                if (candidates != null)
                    return candidates.Url;
            }
            // اگه srcset نبود یا خالی بود → src معمولی
            return img.GetAttributeValue("src", null)
                   ?? img.GetAttributeValue("data-src", null)
                   ?? img.GetAttributeValue("data-lazy-src", null)
                   ?? img.GetAttributeValue("data-original", null);
        }
        private int ExtractWidthFromUrl(string url)
        {
            var m = Regex.Match(url.ToLowerInvariant(), @"(\d{3,})w");
            return m.Success && int.TryParse(m.Groups[1].Value, out var w) ? w : 0;
        }
        private int CalculateImageScore(string url, HtmlNode img, string host)
        {
            int score = 100;
            var lower = url.ToLowerInvariant();
            // تشخیص عرض از URL — هم xxxx x xxxx و هم xxxxw
            int detectedWidth = 0;
            var sizeMatch = Regex.Match(lower, @"(\d{3,})x(\d{3,})");
            if (sizeMatch.Success && int.TryParse(sizeMatch.Groups[1].Value, out var w1))
                detectedWidth = w1;
            var wMatch = Regex.Match(lower, @"(\d{3,})w");
            if (wMatch.Success && int.TryParse(wMatch.Groups[1].Value, out var w2) && w2 > detectedWidth)
                detectedWidth = w2;
            // امتیاز بر اساس عرض واقعی
            if (detectedWidth >= 1200) score += 20000;
            else if (detectedWidth >= 1000) score += 16000;
            else if (detectedWidth >= 800) score += 12000;
            else if (detectedWidth >= 600) score += 8000;
            else if (detectedWidth >= 400) score += 4000;
            // داخل figure = عکس اصلی خبر (خبرورزشی همیشه اینجوری می‌کنه!)
            if (img.ParentNode.Name == "figure" || img.Ancestors("figure").Any())
                score += 25000; // بالاترین امتیاز ممکن
                                // کلاس‌های خبرورزشی
            var classId = (img.GetAttributeValue("class", "") + " " + img.GetAttributeValue("id", "")).ToLowerInvariant();
            if (classId.Contains("main") || classId.Contains("photo") || classId.Contains("image") || classId.Contains("lead"))
                score += 15000;
            // جریمه سنگین برای og:image در خبرورزشی (همیشه لوگوئه!)
            if (host.Contains("khabarvarzeshi.com") || host.Contains("khabarvarzeshi.ir"))
            {
                if (img.Name == "meta" || lower.Contains("logo") || lower.Contains("default") || lower.Contains("favicon"))
                    return 1; // کاملاً حذفش کن!
            }
            // جریمه سنگین برای گیف (۹۹٪ تبلیغ یا لوگو متحرک در خبرورزشی!)
            if (lower.EndsWith(".gif"))
            {
                // اگه از متا تگ اومده → ۱۰۰٪ تبلیغ
                if (img.Name == "meta") return 0;
                // اگه از محتوا اومده ولی داخل figure نباشه → احتمالاً تبلیغ
                if (!img.Ancestors("figure").Any())
                    score -= 40000;
                // در کل گیف‌ها امتیاز خیلی کم می‌گیرن
                score -= 20000;
            }
            // جریمه لوگو و تبلیغ
            if (IsBadImage(lower))
                score -= 50000;
            return Math.Max(score, 0);
        }
        private bool IsValidMainImage(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            var lower = url.ToLowerInvariant();
            // حذف کوئری استرینگ قبل از بررسی پسوند
            var cleanUrl = lower.Split('?')[0];
            // بررسی پسوند واقعی فایل
            bool hasValidExtension = cleanUrl.EndsWith(".jpg") ||
                                     cleanUrl.EndsWith(".jpeg") ||
                                     cleanUrl.EndsWith(".png") ||
                                     cleanUrl.EndsWith(".webp");
            if (!hasValidExtension) return false;
            // حالا بررسی کنیم که لوگو یا تبلیغ نباشه
            return !IsBadImage(lower);
        }
        private bool IsBadImage(string lower)
        {
            string[] bad = { "logo", "icon", "avatar", "banner", "advert", "ads", "ad-", "thumb", "small", "tiny", "50x", "100x", "150x", "200x", "favicon", "profile", "placeholder", "watermark", "sponsor" };
            return bad.Any(k => lower.Contains(k));
        }




        private string NormalizeUrl(string baseUrl, string src)
        {
            if (string.IsNullOrWhiteSpace(src)) return src;
            if (src.StartsWith("http")) return src;
            if (src.StartsWith("//")) return "https:" + src;
            return new Uri(new Uri(baseUrl), src).ToString();
        }

        // HasVideoAsync — سریع، هوشمند و بدون مرورگر

        public async Task<bool> HasVideoAsync(string url, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            // مرحله ۰: تشخیص فوری از روی URL
            if (VideoExtensions.Any(ext => url.Contains(ext, StringComparison.OrdinalIgnoreCase)) ||
                EmbedPlatforms.Any(p => url.Contains(p, StringComparison.OrdinalIgnoreCase)))
                return true;

            try
            {

                (bool, string) httpResult = await _httpClient.TryHtmlForCheckVideoAsync(url, ct);

                if (httpResult.Item1 == false)
                    return false;

                if (httpResult.Item1 && string.IsNullOrEmpty(httpResult.Item2))
                    return true;


                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(httpResult.Item2);
                var root = doc.DocumentNode;

                // ۱. تگ <video> واقعی
                if (root.SelectSingleNode("//video[source] | //video[@src]") != null)
                    return true;

                // ۲. لینک مستقیم به فایل ویدئویی
                var videoLinks = root.SelectNodes("//source[@src] | //a[contains(@href, '.mp4') or contains(@href, '.m3u8')]");
                if (videoLinks?.Any(n =>
                {
                    var src = n.GetAttributeValue("src", "") + n.GetAttributeValue("href", "");
                    return VideoExtensions.Any(ext => src.Contains(ext, StringComparison.OrdinalIgnoreCase));
                }) == true)
                    return true;

                // ۳. iframe واقعی از پلتفرم‌های ویدیویی
                var iframes = root.SelectNodes("//iframe[contains(@src,'embed') or contains(@src,'watch') or contains(@src,'/v/')]");
                if (iframes?.Any(iframe =>
                {
                    var src = iframe.GetAttributeValue("src", "");
                    return EmbedPlatforms.Any(p => src.Contains(p, StringComparison.OrdinalIgnoreCase));
                }) == true)
                    return true;

                return false;
            }
            catch
            {
                return false; // هر خطایی → فرض بر نبود ویدئو (امن‌ترین حالت)
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
