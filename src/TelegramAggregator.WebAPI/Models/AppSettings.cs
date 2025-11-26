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
        public List<string> VideoKeywords { get; set; } = new(); 
        public string[]? LineupKeywords { get; set; }
        public int QueueCapacityPerSource { get; set; } 
        public int MaxSeenKeys { get; set; } 

    }
}
