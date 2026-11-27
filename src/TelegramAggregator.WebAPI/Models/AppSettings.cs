using TelegramAggregator.WebAPI.Models.FootbalSettings;
using TelegramAggregator.WebAPI.Models.LiveBoard;

namespace TelegramAggregator.WebAPI.Models
{
    public class AppSettings
    {
        public FootbalForiSettings FootbalForiSettings { get; set; }
        public LiveBoardSettings LiveBoardSettings { get; set; } = new();
    }
}
