namespace TelegramAggregator.WebAPI.Models
{
    public class FilterConfig
    {
        public List<string> AllowedSports { get; set; } = new();
        public int FootballWeight { get; set; } = 100; // 100 = فقط فوتبال
    }
}
