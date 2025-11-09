using TelegramAggregator.Core;
using TelegramAggregator.WebAPI.Core;
using TelegramAggregator.WebAPI.Infrastructure;

namespace TelegramAggregator.WebAPI.Steps;
public class FetchRssStep : IPipelineStep
{
    private readonly HttpHelper _httpHelper;
    private readonly RssParser _rssParser;
    private readonly string _rssUrl;

    public FetchRssStep(HttpHelper httpHelper, RssParser rssParser, string rssUrl)
    {
        _httpHelper = httpHelper;
        _rssParser = rssParser;
        _rssUrl = rssUrl;
    }

    public async Task ExecuteAsync(PipelineContext context)
    {
        try
        {
            var rssContent = await _httpHelper.GetStringAsync(_rssUrl);
            context.FeedItems = _rssParser.Parse(rssContent);
        }
        catch (Exception ex)
        {
            context.ErrorMessage = $"خطا در واکشی RSS: {ex.Message}";
        }
    }
}

