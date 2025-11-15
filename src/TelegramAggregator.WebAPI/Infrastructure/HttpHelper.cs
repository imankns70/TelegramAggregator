using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace TelegramAggregator.WebAPI.Infrastructure;

/// <summary>
/// ابزار کمکی HTTP با پشتیبانی از Encoding، لاگ و CancellationToken
/// </summary>
public class HttpHelper : IHttpHelper
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpHelper> _logger;

    public HttpHelper(IHttpClientFactory factory, ILogger<HttpHelper> logger)
    {
        _logger = logger;
        _httpClient = factory.CreateClient("RssHttpClient");
    }

    public async Task<string?> GetStringAsync(string url, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("درخواست ناموفق ({StatusCode}) برای URL: {Url}", response.StatusCode, url);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            var encoding = DetectEncoding(response.Content.Headers) ?? Encoding.UTF8;
            return encoding.GetString(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطا در دریافت داده از {Url}", url);
            return null;
        }
    }

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

    private static Encoding? DetectEncoding(HttpContentHeaders headers)
    {
        try
        {
            var charset = headers.ContentType?.CharSet;
            return string.IsNullOrEmpty(charset) ? null : Encoding.GetEncoding(charset);
        }
        catch
        {
            return null;
        }
    }
}