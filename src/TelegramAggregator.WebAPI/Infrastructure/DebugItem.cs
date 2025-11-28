namespace TelegramAggregator.WebAPI.Infrastructure
{

    public class DebugItem
    {
        public string SourceName { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Link { get; set; }
        public DateTimeOffset PubDate { get; set; }
        public DateTime ProcessedAt { get; set; } = DateTime.Now;
        public DateTime? PublishedAt { get; set; }
        public bool IsAccepted { get; set; }
        public string? PublishStatus { get; set; }
        public string? RejectReason { get; set; }
        public string PreviewHtml { get; set; } = "";

    }
}





