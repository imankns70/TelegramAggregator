namespace TelegramAggregator.WebAPI.Models
{

    public class ChannelConfig
    {
        public string Name { get; set; } = "";
        public long ChannelId { get; set; }
        public string Template { get; set; } = "";
        public List<SourceConfig> Sources { get; set; } = new();
        public string AdminUsername { get; set; } = "";
        public string ChannelUsername { get; set; } = "";

    }
}
