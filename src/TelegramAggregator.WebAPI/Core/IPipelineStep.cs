using TelegramAggregator.WebAPI.Core;

namespace TelegramAggregator.Core
{
    public interface IPipelineStep
    {
        /// <summary>
        /// اجرای یک مرحله روی PipelineContext
        /// </summary>
        Task ExecuteAsync(PipelineContext context);
    }
}
