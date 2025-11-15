using System.ServiceModel.Syndication;
using System.Xml;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure;

/// <summary>
/// پارس کردن محتوای RSS و تبدیل به لیست NewsItem
/// </summary>
public class RssParser : IRssParser
{
    /// <summary>
    /// پارس کردن محتوای RSS و تبدیل به لیست NewsItem
    /// </summary>
    /// <param name="rssContent">محتوای RSS به صورت XML</param>
    /// <returns>لیست NewsItem</returns>
    public List<NewsItem> Parse(string rssContent)
    {
        var items = new List<NewsItem>();

        if (string.IsNullOrWhiteSpace(rssContent))
            return items;

        try
        {
            using var reader = XmlReader.Create(new StringReader(rssContent));
            var feed = SyndicationFeed.Load(reader);

            if (feed == null) return items;

            items = feed.Items
                .Where(a => !a.Title.Text.Contains("ویدیو", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(i => i.PublishDate.DateTime)
                .Take(5)
                .Select(entry => new NewsItem
                {
                    Title = entry.Title?.Text ?? "",
                    ShortDescription = entry.Summary?.Text ?? "",
                    Link = entry.Links.FirstOrDefault()?.Uri?.ToString() ?? "",
                    PubDate = entry.PublishDate.DateTime,
                    SourceName = feed.Title?.Text ?? "RSS"
                })
                .ToList();
        }
        catch (Exception ex)
        {
            // لاگ خطا (اختیاری)
            // _logger.LogError(ex, "خطا در پارس RSS");
        }

        return items;
    }
}