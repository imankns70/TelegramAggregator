using Microsoft.AspNetCore.Http;

namespace TelegramAggregator.WebAPI.Models.FootbalSettings
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
        public List<string> WomenKeywords { get; set; } 
        public List<SourceConfig> Sources { get; set; }

        //scoring
        public ScoringConfig Scoring { get; set; } = new();
        public Dictionary<string, List<ScoredItem>> Teams { get; set; } = new();
        public Dictionary<string, List<ScoredItem>> Celebrities { get; set; } = new();
        public Dictionary<string, List<ScoredItem>> Coaches { get; set; } = new();
        public List<ScoredItem> Commentators { get; set; } = new();
        public List<string> TransferKeywords { get; set; } = new();
        public List<string> SevereInjuryKeywords { get; set; } = new();
        public List<string> ExclusiveTags { get; set; } = new(); 
    }
}
