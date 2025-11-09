using TelegramAggregator.Core;
using TelegramAggregator.WebAPI.Core;
using TelegramAggregator.WebAPI.Models.TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Steps
{

    public class UpdateQueueStep : IPipelineStep
    {
        public Task ExecuteAsync(PipelineContext context)
        {
            // این مرحله می‌تواند آیتم‌ها را در صف انتشار ذخیره کند
            context.Queue = new Queue<NewsItem>(context.ParsedItems);
            return Task.CompletedTask;
        }
    }

}
