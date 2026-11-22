using System.ServiceModel.Syndication;
using System.Xml;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Services;

public class RssParserService : IRssParserService
{
    private readonly ILogger<RssParserService> _logger;

    public RssParserService(ILogger<RssParserService> logger)
    {
        _logger = logger;
    }

    public IEnumerable<NewsItem> Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Array.Empty<NewsItem>();
        }

        try
        {
            using var input = new StringReader(xml);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            var feed = SyndicationFeed.Load(reader);
            var cutoff = DateTimeOffset.UtcNow.AddHours(-6);

            return feed.Items
                .Select(entry => new NewsItem
                {
                    Title = TextCleaner.CleanTitle(entry.Title?.Text),
                    ShortDescription = TextCleaner.CleanShortDescription(entry.Summary?.Text),
                    Link = entry.Links.FirstOrDefault(link => link.RelationshipType == "alternate")?.Uri?.ToString()
                        ?? entry.Links.FirstOrDefault()?.Uri?.ToString()
                        ?? string.Empty,
                    PubDate = (entry.PublishDate == DateTimeOffset.MinValue
                        ? entry.LastUpdatedTime
                        : entry.PublishDate).ToUniversalTime(),
                    SourceName = feed.Title?.Text ?? "RSS"
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Title)
                    && Uri.TryCreate(item.Link, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                    && item.PubDate >= cutoff
                    && item.PubDate <= DateTimeOffset.UtcNow.AddMinutes(5))
                .OrderBy(item => item.PubDate)
                .ToList();
        }
        catch (Exception exception) when (exception is XmlException || exception is FormatException
            || exception is ArgumentException)
        {
            _logger.LogError(exception, "Unable to parse RSS feed");
            return Array.Empty<NewsItem>();
        }
    }
}
