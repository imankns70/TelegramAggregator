using TelegramAggregator.Core;
using TelegramAggregator.Infrastructure;
using TelegramAggregator.WebAPI.Core;
namespace TelegramAggregator.WebAPI.Steps
{


    public class FetchFullContentStep : IPipelineStep
    {
        private readonly HtmlExtractor _htmlExtractor;

        public FetchFullContentStep(HtmlExtractor htmlExtractor)
        {
            _htmlExtractor = htmlExtractor;
        }

        public async Task ExecuteAsync(PipelineContext context)
        {
            foreach (var item in context.ParsedItems)
            {
                if (string.IsNullOrEmpty(item.Link))
                    continue;

                try
                {
                    item.FullDescription = await _htmlExtractor.GetFullDescriptionAsync(item.Link);
                    item.ImageUrl = await _htmlExtractor.ExtractMainImageAsync(item.Link);
                }
                catch (Exception ex)
                {
                    item.ErrorMessage = $"خطا در واکشی محتوا یا تصویر: {ex.Message}";
                }
;
            }
        }
    }

}
