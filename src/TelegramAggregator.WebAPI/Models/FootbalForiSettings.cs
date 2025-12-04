using Microsoft.AspNetCore.Http;

namespace TelegramAggregator.WebAPI.Models
{
    public class FootbalForiSettings
    {
        public string Name { get; set; }
        public long ChannelId { get; set; }
        public string ChannelUsername { get; set; }
        public string AdminUsername { get; set; }
        public int QueueCapacityPerSource { get; set; }
        public int MaxSeenKeys { get; set; }
        public string BotToken { get; set; }
        public string BotUserName { get; set; }
        public string BotNameDev { get; set; } 
        public List<string> VideoKeywords { get; set; }
        public List<string> OtherSportsKeywords { get; set; } 
        public List<SourceConfig> Sources { get; set; }
    }
}
