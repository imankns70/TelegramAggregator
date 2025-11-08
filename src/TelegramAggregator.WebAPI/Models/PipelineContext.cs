using TelegramAggregator.WebAPI.Models.TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Models
{
    /// <summary>
    /// شامل داده‌های مشترک در کل فرآیند ارسال یک خبر (درون یک پایپ‌لاین)
    /// </summary>
    public class PipelineContext
    {
        /// <summary>
        /// منبع خبر (مثلاً Varzesh3، FarsNews یا گروه ادمین‌ها)
        /// </summary>
        public NewsSource Source { get; set; }

        /// <summary>
        /// مدل کامل خبر (شامل تیتر، توضیح کوتاه، توضیح کامل، لینک و...)
        /// </summary>
        public NewsItem News { get; set; } = default!;

        /// <summary>
        /// وضعیت اینکه آیا این خبر از گروه ادمین‌ها آمده یا از جمع‌آورنده
        /// </summary>
        public bool IsFromAdminGroup => Source == NewsSource.AdminGroup;

        /// <summary>
        /// آیا این خبر تکراری است (با استفاده از DuplicateFilter تعیین می‌شود)
        /// </summary>
        public bool IsDuplicate { get; set; }

        /// <summary>
        /// قالب نهایی پیام که باید به کانال ارسال شود (بعد از فرمت نهایی)
        /// </summary>
        public string? FormattedMessage { get; set; }

        /// <summary>
        /// در صورت وجود تصویر در خبر
        /// </summary>
        public string? ImageUrl => News.ImageUrl;

        /// <summary>
        /// در صورت وجود ویدیو در خبر
        /// </summary>
        public string? VideoUrl => News.VideoUrl;

        /// <summary>
        /// زمان ایجاد یا پردازش خبر در پایپ‌لاین
        /// </summary>
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// پیام خطا یا هشدار در حین پردازش (اختیاری)
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// شناسه پیام ارسال‌شده به کانال (در صورت نیاز برای حذف یا بروزرسانی)
        /// </summary>
        public int? SentMessageId { get; set; }
    }
}
