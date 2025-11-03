namespace TelegramAggregator.WebAPI.Models
{
    public class AppSettings
    {
        public List<string> FootballKeywords { get; set; } = new();
        public List<string> NonSportKeywords { get; set; } = new();
        public List<string> ForbiddenTitleKeywords { get; set; } = new();
    }
}
