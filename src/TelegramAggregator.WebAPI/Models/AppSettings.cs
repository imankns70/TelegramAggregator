namespace TelegramAggregator.WebAPI.Models
{
    public class AppSettings
    {
        public string BotToken { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public int PollIntervalMinutes { get; set; }
        public long HiddenGroupId { get; set; }
        public List<ChannelConfig> Channels { get; set; } = new();
        public List<string> FootballKeywords { get; set; } = new();
        public List<string> NonSportKeywords { get; set; } = new();
        public string[]? LineupKeywords { get; set; }
        public int PublishQueueCapacity { get; set; } 
        public int MaxItemsPerSource { get; set; } 

    }
}
