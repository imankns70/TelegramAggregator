namespace TelegramAggregator.WebAPI.Models
{
    public class SourceConfig
    {
        /// <summary>نام منبع (مثلاً ورزش3)</summary>
        public string Name { get; set; } = "";

        /// <summary>آدرس RSS یا URL سایت</summary>
        public string Url { get; set; } = "";

    }
}
