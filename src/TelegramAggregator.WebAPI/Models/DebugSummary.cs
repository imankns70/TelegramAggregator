namespace TelegramAggregator.WebAPI.Models
{
    public class DebugSummary
    {
        public int TotalProcessed { get; set; }
        public int AcceptedCount { get; set; }
        public int RejectedCount { get; set; }
        public int PublishedCount { get; set; }
        public int InQueueCount { get; set; }
        public double AcceptanceRate { get; set; }
        public string SystemStatus { get; set; } = "Unknown";
        public string StatusColor { get; set; } = "green";
        public string QueueDetails { get; set; } = "Empty";
        public IReadOnlyDictionary<string, int> QueueBySource { get; set; }
            = new Dictionary<string, int>();

    }
}