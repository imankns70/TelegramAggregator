using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Services;

/// <summary>
/// رابط پارس RSS
/// </summary>
public interface IRssParserService
{
    /// <summary>
    /// پارس کردن محتوای RSS و تبدیل به لیست NewsItem
    /// </summary>
    /// <param name="rssContent">محتوای RSS به صورت XML</param>
    /// <returns>لیست NewsItem</returns>
    public IEnumerable<NewsItem> Parse(string xml);
}