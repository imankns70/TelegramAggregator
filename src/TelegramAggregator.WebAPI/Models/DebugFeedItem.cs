namespace TelegramAggregator.WebAPI.Models
{
    public class DebugFeedItem
    {
        public string SourceName { get; set; }
        public string Title { get; set; }
        public string Link { get; set; }
        public DateTimeOffset PubDate { get; set; }
        public bool IsAccepted { get; set; }
        public string RejectReason { get; set; }
        public DateTimeOffset ProcessedAt { get; set; }
    }
}
