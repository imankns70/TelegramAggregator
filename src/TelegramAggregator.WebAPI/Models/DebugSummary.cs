namespace TelegramAggregator.WebAPI.Models
{
    public class DebugSummary
    {
        public int TotalProcessed { get; set; }
        public int AcceptedCount { get; set; }
        public int RejectedCount { get; set; }
        public int PublishedCount { get; set; }
        public int InQueueCount { get; set; }
    }
}
