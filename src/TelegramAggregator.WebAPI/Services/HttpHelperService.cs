using AngleSharp.Dom;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace TelegramAggregator.WebAPI.Services;

/// <summary>
/// ابزار کمکی HTTP با پشتیبانی از Encoding، لاگ و CancellationToken
/// </summary>
public class HttpHelperService : IHttpHelperService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpHelperService> _logger;

    public HttpHelperService(HttpClient httpClient, ILogger<HttpHelperService> logger)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        try
        {
            // این خط مهمه! از کلاینت تنظیم‌شده استفاده کن
             

            var requestUrl = $"{url}?_={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var response = await _httpClient.GetAsync(requestUrl, ct);

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

 

    private static Encoding DetectEncoding(HttpContentHeaders headers)
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



    public async Task<(bool,string)> TryHtmlForCheckVideoAsync(string url, CancellationToken ct)
    {
        var separator = url.Contains('?') ? "&" : "?";
        var requestUrl = $"{url}{separator}nocache={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 60_000); // فقط 60KB اول

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode) return (false,"");

        // اگر خودش فایل ویدئوست → ۱۰۰٪
        if (response.Content.Headers.ContentType?.MediaType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true)
            return (true,"");

        var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        var html = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(html)) return (false, "");

        return (true, html);
    }
}