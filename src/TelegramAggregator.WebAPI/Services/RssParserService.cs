using System.Collections.Concurrent;
using System.Net;
using System.ServiceModel.Syndication;
using System.Xml;
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
    public RssParserService(ILogger<RssParserService> logger, IDebugStore debugStore)
    {
        _logger = logger;
        _debugStore = debugStore;
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
                    var rawTitle = entry.Title?.Text ?? "";
                    var rawSummary = entry.Summary?.Text ?? rawTitle;
              
                    return new NewsItem
                    {
                        Title = TextCleaner.CleanTitle(rawTitle),
                        ShortDescription = TextCleaner.CleanShortDescription(rawSummary),
                        Link = entry.Links.FirstOrDefault()?.Uri?.ToString() ?? "",
                        PubDate = entry.PublishDate.DateTime,
                        SourceName = feed.Title?.Text ?? "RSS"
                    };
                }).ToList();


            foreach (var item in result)
            {
                var rawLink = item.Link.Trim();        

                // نرمال‌سازی لینک — این خط همه تکراری‌ها رو میکشه!
                var normalizedLink = NormalizeLink(rawLink);
                _debugStore.LogOrUpdate(new DebugFeedItem
                {
                    Link = normalizedLink,                    // لینک پاک شده                 
                    Title = item.Title,
                    SourceName = feedTitle,
                    PubDate = item.PubDate.DateTime,
                    ProcessedAt = DateTime.UtcNow,
                    PublishStatus = "جدید_واکشی_شده"
                });

            }


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
    private static string NormalizeLink(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        try
        {
            var uri = new Uri(url, UriKind.Absolute);

            // حذف www.
            var host = uri.Host;
            if (host.StartsWith("www."))
                host = host["www.".Length..];

            // ساخت لینک پاک
            var builder = new UriBuilder
            {
                Scheme = uri.Scheme.ToLower(),
                Host = host,
                Port = uri.Port == 80 || uri.Port == 443 ? -1 : uri.Port,
                Path = uri.AbsolutePath.TrimEnd('/'),  // حذف / آخر
                Query = uri.Query,
                Fragment = uri.Fragment
            };

            return builder.Uri.ToString();
        }
        catch
        {
            // اگه لینک خراب بود، حداقل / آخر رو حذف کن
            return url.TrimEnd('/').Trim();
        }
    }
}