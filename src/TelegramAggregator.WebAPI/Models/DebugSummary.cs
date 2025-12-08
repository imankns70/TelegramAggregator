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


        public int LastHourItemsTotal { get; set; }
        public int LastHourAccepted { get; set; }
        public int LastHourRejected { get; set; }
        public double LastHourAcceptanceRate { get; set; }

        public int TodayItemsTotal { get; set; }
        public int TodayAccepted { get; set; }
        public int TodayRejected { get; set; }
        public double TodayAcceptanceRate { get; set; }
       

        public string DashboardStatusColor { get; set; } = "Unknown";
        public string SystemStatus { get; set; } = "Unknown";
        public string StatusColor { get; set; } = "green";
        public string QueueDetails { get; set; } = "Empty";
  

    }
}