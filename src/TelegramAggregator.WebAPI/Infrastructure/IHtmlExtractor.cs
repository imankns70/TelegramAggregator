namespace TelegramAggregator.Infrastructure;

/// <summary>
/// رابط استخراج محتوا و تصویر از صفحات HTML
/// </summary>
public interface IHtmlExtractor
{
    /// <summary>
    /// استخراج متن کامل خبر (بدون تبلیغات، اسکریپت و ...)
    /// </summary>
    /// <param name="url">آدرس صفحه خبر</param>
    /// <param name="ct">توکن لغو عملیات</param>
    /// <returns>متن تمیز خبر</returns>
    Task<string> GetFullDescriptionAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// استخراج تصویر اصلی خبر (og:image یا اولین تصویر معتبر)
    /// </summary>
    /// <param name="url">آدرس صفحه خبر</param>
    /// <param name="ct">توکن لغو عملیات</param>
    /// <returns>آدرس تصویر یا null</returns>
    Task<string?> ExtractMainImageAsync(string url, CancellationToken ct = default);

    Task<bool> HasVideoAsync(string url, CancellationToken ct = default);
}