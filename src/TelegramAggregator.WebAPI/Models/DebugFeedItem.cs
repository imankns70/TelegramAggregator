namespace TelegramAggregator.WebAPI.Models
{
    public class DebugFeedItem
    {
        public string? SourceName { get; set; }
        public string? Title { get; set; }
        public string? Link { get; set; }
        public DateTimeOffset PubDate { get; set; }
        public bool IsAccepted { get; set; }
        public string? RejectReason { get; set; }

        // جدید: وضعیت انتشار
        public string? PublishStatus { get; set; } // "در صف انتشار" | "ارسال شد" | null
        public DateTimeOffset? PublishedAt { get; set; } // زمان دقیق ارسال به تلگرام
        public DateTimeOffset ProcessedAt { get; set; }
        // این فیلد جدید — کلید منحصر به فرد خبر
        public string UniqueKey => $"{Link}-{PubDate:yyyyMMddHHmm}";
    }
}
