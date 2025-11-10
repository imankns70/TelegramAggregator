using TelegramAggregator.Core;
using TelegramAggregator.WebAPI.Core;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models.TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Steps;

public class FetchAndParseRssStep : IPipelineStep
{
    private readonly HttpHelper _httpHelper;
    private readonly RssParser _rssParser;
    private readonly string _rssUrl;

    public FetchAndParseRssStep(HttpHelper httpHelper, RssParser rssParser, string rssUrl)
    {
        _httpHelper = httpHelper;
        _rssParser = rssParser;
        _rssUrl = rssUrl;
    }

    public async Task ExecuteAsync(PipelineContext context)
    {
        try
        {
            // واکشی محتوا
            var rssContent = await _httpHelper.GetStringAsync(_rssUrl);

            // پارس RSS و تبدیل به NewsItem
            var feedItems = _rssParser.Parse(rssContent);

            context.ParsedItems = feedItems
                .Select(item => new NewsItem
                {
                    Title = item.Title,
                    Link = item.Link,
                    PubDate = item.PubDate,
                    SourceName = item.SourceName
                })
                .ToList();
        }
        catch (Exception ex)
        {
            context.ErrorMessage = $"خطا در واکشی و پارس RSS: {ex.Message}";
        }
    }
}
