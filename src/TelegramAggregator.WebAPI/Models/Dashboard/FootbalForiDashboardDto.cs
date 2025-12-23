namespace TelegramAggregator.WebAPI.Models.Dashboard
{
    public class FootbalForiDashboardDto
    {
        public int TotalProcessed { get; set; }
        public int AcceptedCount { get; set; }
        public int RejectedCount { get; set; }
        public int PublishedCount { get; set; }
        public int InQueueCount { get; set; }
        public double AcceptanceRate { get; set; }
        public string DashboardStatusColor { get; set; } = "Unknown";
        public string SystemStatus { get; set; } = "Unknown";
        public string StatusColor { get; set; } = "green";
        public string QueueDetails { get; set; } = "Empty";
        public string UtcNow { get; set; } 
        public string IranNow { get; set; } 
        public string ServerLocal { get; set; } 


        public Dictionary<string, List<QueueFootbalForiDashboardDto>> Queues { get; set; }
        public List<QueueFootbalForiDashboardDto> PublishedItems { get; set; } = new List<QueueFootbalForiDashboardDto>();
        public List<QueueFootbalForiDashboardDto> RecentProcessed { get; set; } = new List<QueueFootbalForiDashboardDto>();

    }
}
