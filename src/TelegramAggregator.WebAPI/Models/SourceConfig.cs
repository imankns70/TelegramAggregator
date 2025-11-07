namespace TelegramAggregator.WebAPI.Models
{
    public class SourceConfig
    {
        /// <summary>نام منبع (مثلاً ورزش3)</summary>
        public string Name { get; set; } = "";

        /// <summary>آدرس RSS یا URL سایت</summary>
        public string Url { get; set; } = "";

        /// <summary>نوع منبع (RSS, HTML و غیره)</summary>
        public string Type { get; set; } = "Rss";
    }
}
