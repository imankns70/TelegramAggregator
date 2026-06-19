namespace TelegramAggregator.WebAPI.Services;

/// <summary>
/// رابط استخراج محتوا و تصویر از صفحات HTML
/// </summary>
public interface IFullDecriptionExtractorService
{
    /// <summary>
    /// استخراج متن کامل خبر (بدون تبلیغات، اسکریپت و ...)
    /// </summary>
    /// <param name="url">آدرس صفحه خبر</param>
    /// <param name="ct">توکن لغو عملیات</param>
    /// <returns>متن تمیز خبر</returns>
    Task<string> GetFullDescriptionAsync(string url, CancellationToken ct = default);
}