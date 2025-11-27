using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, DebugFeedItem> _debugItems;
    public RssParser(ILogger<RssParser> logger, NewsStorage newsStorage)
    {
        _logger = logger;
        _debugItems= newsStorage._debugItems;
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
            var feedTitle = feed.Title?.Text ?? "RSS";


            result = feed.Items
                .Where(a => !a.Title.Text.Contains("ویدیو", StringComparison.OrdinalIgnoreCase))
                .Where(item => item.PublishDate >= cutoff)
              .Where(item =>
              {
                  var rawLink = item.Links.FirstOrDefault()?.Uri?.ToString()?.Trim();
                  if (string.IsNullOrWhiteSpace(rawLink))
                      return false;

                  // نرمال‌سازی لینک — این خط همه تکراری‌ها رو میکشه!
                  var normalizedLink = NormalizeLink(rawLink);

                  return _debugItems.TryAdd(normalizedLink, new DebugFeedItem
                  {
                      Link = normalizedLink,                    // لینک پاک شده                 
                      Title = item.Title?.Text ?? "",
                      SourceName = feedTitle,
                      PubDate = item.PublishDate.DateTime,
                      ProcessedAt = DateTime.UtcNow,
                      PublishStatus = "جدید_واکشی_شده"
                  });
              })
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