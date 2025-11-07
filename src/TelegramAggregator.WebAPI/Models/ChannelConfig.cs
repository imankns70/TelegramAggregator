namespace TelegramAggregator.WebAPI.Models
{
    public class ChannelConfig
    {
        /// <summary>نام نمایشی کانال</summary>
        public string Name { get; set; } = "";

        /// <summary>آیدی عددی کانال</summary>
        public long ChannelId { get; set; }

        /// <summary>یوزرنیم بدون @</summary>
        public string ChannelUsername { get; set; } = "";

        /// <summary>یوزرنیم مدیر یا ادمین کانال</summary>
        public string AdminUsername { get; set; } = "";

        /// <summary>قالب پیام ارسالی</summary>
        public string Template { get; set; } = "{Title}\n\n{Description}\n\n@{ChannelUsername}\n#{AdminUsername}";

        /// <summary>لیست منابع خبری (RSS و ...)</summary>
        public List<SourceConfig> Sources { get; set; } = new();
    }
}
