using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Models.TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Core
{
    /// <summary>
    /// شامل داده‌ها و وضعیت جاری در کل فرآیند پردازش خبر (pipeline context)
    /// </summary>
    public class PipelineContext
    {
        /// <summary>
        /// منبع خبر (مثل Varzesh3، Tarafdari یا گروه ادمین‌ها)
        /// </summary>
        public NewsSource Source { get; set; }

        /// <summary>
        /// مدل خبر شامل عنوان، توضیحات، لینک و ...
        /// برای مرحله Admin pipeline یا یک آیتم RSS واحد
        /// </summary>
        public required NewsItem News { get; set; }

        /// <summary>
        /// لیست آیتم‌هایی که RSS Fetch/Parse برگردانده
        /// برای مرحله RSS pipeline
        /// </summary>
        public List<NewsItem> ParsedItems { get; set; } = new List<NewsItem>();

        /// <summary>
        /// آیتم‌های خام RSS قبل از Parse شدن (اختیاری)
        /// </summary>
        public List<NewsItem>? FeedItems { get; set; } = new List<NewsItem>();

        /// <summary>
        /// آیا خبر از گروه ادمین‌ها آمده است؟
        /// </summary>
        public bool IsFromAdminGroup => Source == NewsSource.AdminGroup;

        /// <summary>
        /// آیا خبر تکراری است؟ (بر اساس DuplicateChecker)
        /// </summary>
        public bool IsDuplicate { get; set; }

        /// <summary>
        /// تنظیمات کانال جاری (مثل قالب پیام و نام کاربری کانال)
        /// </summary>
        public ChannelConfig? ChannelConfig { get; set; }

        /// <summary>
        /// شناسه چت تلگرام برای ارسال پیام
        /// </summary>
        public long ChatId => ChannelConfig?.ChannelId ?? 0;

        /// <summary>
        /// قالب نهایی پیام آماده ارسال به کانال
        /// </summary>
        public string? FormattedMessage { get; set; }

        /// <summary>
        /// زمان پردازش خبر در پایپ‌لاین
        /// </summary>
        public DateTime ProcessedAt { get; init; } = DateTime.UtcNow;
        /// <summary>
        /// صف انتشار خبرها در مراحل بعدی Pipeline
        /// </summary>
        public Queue<NewsItem> Queue { get; set; } = new Queue<NewsItem>();
        /// <summary>
        /// پیام خطا یا هشدار در طول پردازش Pipeline (اختیاری)
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// شناسه پیام ارسال‌شده در تلگرام (در صورت نیاز برای ویرایش/حذف)
        /// </summary>
        public int? SentMessageId { get; set; }

        /// <summary>
        /// کمکی: برای لاگ و دیباگ بهتر
        /// </summary>
        public override string ToString() =>
            $"{Source}: {News?.Title ?? "NoTitle"} @ {ProcessedAt:HH:mm:ss}";
    }
}
