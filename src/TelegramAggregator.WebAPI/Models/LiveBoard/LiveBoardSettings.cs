namespace TelegramAggregator.WebAPI.Models.LiveBoard
{
    public class LiveBoardSettings
    {
        public bool Enabled { get; set; }
        public long ChannelId { get; set; }
        public int MessageId { get; set; }
        public const int DefaultLiveRefreshSeconds = 10;

        public int LiveRefreshSeconds { get; set; } = DefaultLiveRefreshSeconds;
        public int OfflineRefreshMinutes { get; set; } = 5;
    }
}
