namespace TelegramAggregator.WebAPI.Models
{
    public class ContentItem
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Link { get; set; } = "";
        public DateTime PubDate { get; set; }
        public string? ImageUrl { get; set; }
        public string? VideoUrl { get; set; }
        public bool HasMedia { get; set; }
    }
}
