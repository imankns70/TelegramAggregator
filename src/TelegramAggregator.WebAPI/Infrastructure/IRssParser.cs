using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure;

/// <summary>
/// رابط پارس RSS
/// </summary>
public interface IRssParser
{
    /// <summary>
    /// پارس کردن محتوای RSS و تبدیل به لیست NewsItem
    /// </summary>
    /// <param name="rssContent">محتوای RSS به صورت XML</param>
    /// <returns>لیست NewsItem</returns>
    List<NewsItem> Parse(string rssContent);
}