using TelegramAggregator.Core;
using TelegramAggregator.WebAPI.Core;
using TelegramAggregator.WebAPI.Models.TelegramAggregator.WebAPI.Models;


namespace TelegramAggregator.WebAPI.Steps;
public class ParseFeedStep : IPipelineStep
{
    public Task ExecuteAsync(PipelineContext context)
    {
        // در این مرحله می‌توانید فیلدهای مورد نیاز مثل Title, Link, PubDate و UniqueKey را استخراج کنید
        context.ParsedItems = context.FeedItems
            .Select(item => new NewsItem
            {
                Title = item.Title,
                Link = item.Link,
                PubDate = item.PubDate,
                SourceName = item.SourceName // UniqueKey از این و Link ساخته می‌شود
            }).ToList();

        return Task.CompletedTask;
    }
}

