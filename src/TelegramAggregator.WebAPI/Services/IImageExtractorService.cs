namespace TelegramAggregator.WebAPI.Services;

/// <summary>
/// رابط استخراج محتوا و تصویر از صفحات HTML
/// </summary>
public interface IImageExtractorService
{
    

    /// <summary>
    /// استخراج تصویر اصلی خبر (og:image یا اولین تصویر معتبر)
    /// </summary>
    /// <param name="url">آدرس صفحه خبر</param>
    /// <param name="ct">توکن لغو عملیات</param>
    /// <returns>آدرس تصویر یا null</returns>
    Task<string?> ExtractMainImageAsync(string url, CancellationToken ct = default);

    Task<bool> HasVideoAsync(string url, CancellationToken ct = default);
}