namespace TelegramAggregator.WebAPI.Models
{
    public class ContentItem
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Link { get; set; } = "";
        public DateTime PubDate { get; set; }

        // رسانه‌ها
        public string? ImageUrl { get; set; }
        public string? VideoUrl { get; set; }

        // برای راحتی تصمیم‌گیری در ارسال
        public bool HasImage => !string.IsNullOrEmpty(ImageUrl);
        public bool HasVideo => !string.IsNullOrEmpty(VideoUrl);
        public bool HasMedia => HasImage || HasVideo;

        // برای لاگ و بررسی
        public string SourceName { get; set; } = ""; // مثلا "Varzesh3" یا "Tarafdari"
    }

}
