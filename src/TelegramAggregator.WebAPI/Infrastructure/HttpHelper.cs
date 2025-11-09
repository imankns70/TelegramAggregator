using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    /// <summary>
    /// ابزار کمکی برای دریافت داده از طریق HTTP (مثل RSS و HTML صفحات خبری)
    /// </summary>
    public class HttpHelper
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<HttpHelper> _logger;

        public HttpHelper(ILogger<HttpHelper> logger)
        {
            _logger = logger;

            _httpClient = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true
            });

            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/123.0.0.0 Safari/537.36");
        }

        /// <summary>
        /// دریافت محتوا به صورت string (برای RSS یا HTML)
        /// </summary>
        public async Task<string?> GetStringAsync(string url, CancellationToken ct = default)
        {
            try
            {
                var response = await _httpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("⚠️ درخواست ناموفق ({StatusCode}) برای URL: {Url}", response.StatusCode, url);
                    return null;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                var encoding = DetectEncoding(response.Content.Headers) ?? Encoding.UTF8;

                return encoding.GetString(bytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ خطا در دریافت داده از {Url}", url);
                return null;
            }
        }

        /// <summary>
        /// تشخیص Encoding بر اساس Header پاسخ
        /// </summary>
        private static Encoding? DetectEncoding(HttpContentHeaders headers)
        {
            try
            {
                var charset = headers.ContentType?.CharSet;
                if (string.IsNullOrEmpty(charset))
                    return null;

                return Encoding.GetEncoding(charset);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// بررسی سریع وضعیت دسترسی سایت
        /// </summary>
        public async Task<bool> CheckAvailabilityAsync(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
