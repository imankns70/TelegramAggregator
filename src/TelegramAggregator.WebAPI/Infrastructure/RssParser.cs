using System.Net;
using System.ServiceModel.Syndication;
using System.Xml;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure;

/// <summary>
/// پارس کردن RSS با SyndicationFeed + فیلتر تاریخ + منبع
/// </summary>
public class RssParser : IRssParser
{
    private readonly ILogger<RssParser> _logger;

    public RssParser(ILogger<RssParser> logger)
    {
        _logger = logger;
    }

    public IEnumerable<NewsItem> Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return Enumerable.Empty<NewsItem>();

        var result = new List<NewsItem>();
        XmlReader? reader = null;

        try
        {
            reader = XmlReader.Create(new StringReader(xml));
            var feed = SyndicationFeed.Load(reader);
            if (feed == null) return result;
            var cutoff = DateTimeOffset.Now.AddHours(-6);

         

            result = feed.Items
                .Where(a => !a.Title.Text.Contains("ویدیو", StringComparison.OrdinalIgnoreCase))
                .Where(item => item.PublishDate >= cutoff)
                .OrderByDescending(i => i.PublishDate.DateTime)
                .Take(10)
                .Reverse()
                .Select(entry => new NewsItem
                {
                    Title = entry.Title?.Text ?? "",
                    ShortDescription = entry.Summary?.Text ?? "",
                    Link = entry.Links.FirstOrDefault()?.Uri?.ToString() ?? "",
                    PubDate = entry.PublishDate.DateTime,
                    SourceName = feed.Title?.Text ?? "RSS"
                }).ToList();


        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطا در پارس RSS از منبع: {SourceName}");
        }
        finally
        {
            reader?.Dispose(); // Dispose دستی و ایمن
        }

        return result;
    }
}