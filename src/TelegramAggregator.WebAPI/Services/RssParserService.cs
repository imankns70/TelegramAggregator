using System.Collections.Concurrent;
using System.Net;
using System.ServiceModel.Syndication;
using System.Xml;
using TelegramAggregator.WebAPI.Data.Repositories;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Services;

/// <summary>
/// پارس کردن RSS با SyndicationFeed + فیلتر تاریخ + منبع
/// </summary>
public class RssParserService : IRssParserService
{
    private readonly ILogger<RssParserService> _logger;
    private readonly IDebugStore _debugStore;
    private readonly INewsRepository _newsRepository;
    public RssParserService(ILogger<RssParserService> logger, IDebugStore debugStore,
     INewsRepository newsRepository)
    {
        _logger = logger;
        _debugStore = debugStore;
        _newsRepository = newsRepository;
    }

    public IEnumerable<NewsItem> Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return Enumerable.Empty<NewsItem>();

        var result = new List<NewsItem>();
        XmlReader reader = null;

        try
        {
            reader = XmlReader.Create(new StringReader(xml));
            var feed = SyndicationFeed.Load(reader);
            if (feed == null) return result;
            var cutoff = DateTimeOffset.Now.AddHours(-6);
            var feedTitle = feed.Title?.Text ?? "RSS";

            result = feed.Items
                .OrderByDescending(i => i.PublishDate.DateTime)
                .Take(10)
                .Reverse()
                .Select(entry =>
                {
                    return new NewsItem
                    {
                        Title = TextCleaner.CleanTitle(entry.Title?.Text),
                        ShortDescription = TextCleaner.CleanShortDescription(entry.Summary?.Text),
                        Link = entry.Links.FirstOrDefault()?.Uri?.ToString() ?? "",
                        PubDate = entry.PublishDate.DateTime,
                        SourceName = feed.Title?.Text ?? "RSS"
                    };
                }).ToList();

            _newsRepository.FilterNews(result);
           
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