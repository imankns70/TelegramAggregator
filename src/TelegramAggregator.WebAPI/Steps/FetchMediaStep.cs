using TelegramAggregator.Core;
using TelegramAggregator.WebAPI.Core;

namespace TelegramAggregator.WebAPI.Steps
{
    public class FetchMediaStep : IPipelineStep
    {
        private readonly HttpClient _httpClient;

        public FetchMediaStep(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task ExecuteAsync(PipelineContext context)
        {
            foreach (var item in context.ParsedItems)
            {
                if (string.IsNullOrEmpty(item.Link)) continue;

                try
                {
                    var html = await _httpClient.GetStringAsync(item.Link);
                    var doc = new HtmlAgilityPack.HtmlDocument();
                    doc.LoadHtml(html);

                    // استخراج تصویر
                    var imgNode = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']") ??
                                  doc.DocumentNode.SelectSingleNode("//img[contains(@src,'news') or contains(@src,'picture')]");
                    if (imgNode != null)
                    {
                        var imgUrl = imgNode.GetAttributeValue("content", null) ?? imgNode.GetAttributeValue("src", null);
                        if (!string.IsNullOrWhiteSpace(imgUrl))
                            item.ImageUrl = NormalizeUrl(item.Link, imgUrl);
                    }

                    // استخراج ویدیو (اگر نیاز بود)
                    var videoNode = doc.DocumentNode.SelectSingleNode("//meta[@property='og:video']") ??
                                    doc.DocumentNode.SelectSingleNode("//video/source[@src]") ??
                                    doc.DocumentNode.SelectSingleNode("//video[@src]");
                    if (videoNode != null)
                    {
                        var videoUrl = videoNode.GetAttributeValue("content", null) ?? videoNode.GetAttributeValue("src", null);
                        if (!string.IsNullOrWhiteSpace(videoUrl))
                            item.VideoUrl = NormalizeUrl(item.Link, videoUrl);
                    }
                }
                catch (Exception ex)
                {
                    // ثبت خطا در PipelineContext
                    context.ErrorMessage = $"❗ خطا در واکشی رسانه از {item.Link}: {ex.Message}";
                }
            }
        }

        private static string NormalizeUrl(string baseUrl, string mediaUrl)
        {
            if (mediaUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return mediaUrl;
            try
            {
                var baseUri = new Uri(baseUrl);
                return new Uri(baseUri, mediaUrl).ToString();
            }
            catch
            {
                return mediaUrl;
            }
        }
    }

}
