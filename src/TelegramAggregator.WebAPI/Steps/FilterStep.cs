using TelegramAggregator.Core;
using TelegramAggregator.WebAPI.Core;
using TelegramAggregator.WebAPI.Models;
namespace TelegramAggregator.WebAPI.Steps
{


    public class FilterStep : IPipelineStep
    {
        private readonly DuplicateChecker _duplicateChecker;
        private readonly AppSettings _appSettings;

        public FilterStep(DuplicateChecker duplicateChecker, AppSettings appSettings)
        {
            _duplicateChecker = duplicateChecker;
            _appSettings = appSettings;
        }

        public Task ExecuteAsync(PipelineContext context)
        {
            var footballKeywords = _appSettings.FootballKeywords;
            var nonSportKeywords = _appSettings.NonSportKeywords;

            context.ParsedItems = context.ParsedItems
                .Where(item =>
                    !_duplicateChecker.IsDuplicateOrOld(item) &&     // حذف تکراری‌ها و قدیمی‌ها
                    footballKeywords.Any(k => (item.Title + " " + item.ShortDescription).Contains(k)) &&
                    !nonSportKeywords.Any(k => item.Title.Contains(k) || item.ShortDescription.Contains(k)) &&
                    string.IsNullOrEmpty(item.VideoUrl)             // حذف خبرهای دارای ویدئو
                )
                .OrderByDescending(i => i.PubDate)
                .Take(30)
                .ToList();

            return Task.CompletedTask;
        }
    }


}
