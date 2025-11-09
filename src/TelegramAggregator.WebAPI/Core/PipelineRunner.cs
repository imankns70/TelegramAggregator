using TelegramAggregator.WebAPI.Core;

namespace TelegramAggregator.Core
{
    public class PipelineRunner
    {
        private readonly IEnumerable<IPipelineStep> _steps;

        public PipelineRunner(IEnumerable<IPipelineStep> steps)
        {
            _steps = steps;
        }

        public async Task RunAsync(PipelineContext context)
        {
            foreach (var step in _steps)
            {
                try
                {
                    await step.ExecuteAsync(context);
                    if (context.IsDuplicate)
                        break; // اگر تکراری بود، ادامه مراحل اجرا نشود
                }
                catch (Exception ex)
                {
                    context.ErrorMessage = ex.Message;
                    // در صورت نیاز می‌توان logging هم اینجا انجام داد
                    break;
                }
            }
        }
    }
}
