namespace TelegramAggregator.WebAPI.Models
{
    namespace TelegramAggregator.WebAPI.Models
    {
        public class NewsItem
        {
            /// <summary>عنوان خبر یا پست</summary>
            public string Title { get; set; } = "";

            /// <summary>توضیح کوتاه یا خلاصه محتوا</summary>
            public string ShortDescription { get; set; } = "";

            /// <summary>شرح کامل محتوا</summary>
            public string FullDescription { get; set; } = "";

            /// <summary>لینک منبع اصلی</summary>
            public string Link { get; set; } = "";

            /// <summary>تاریخ انتشار محتوا</summary>
            public DateTime PubDate { get; set; }

            /// <summary>لینک تصویر (در صورت وجود)</summary>
            public string? ImageUrl { get; set; }

            /// <summary>لینک ویدیو (در صورت وجود)</summary>
            public string? VideoUrl { get; set; }

            /// <summary>نوع محتوا: News, Video, Tweet, etc.</summary>
            public string Type { get; set; } = "News";

            /// <summary>نام منبع (مثلاً طرفداری، ورزش3 و ...)</summary>
            public string? SourceName { get; set; }

            /// <summary>کلید یکتا برای جلوگیری از ارسال تکراری</summary>
            public string UniqueKey => $"{SourceName}-{Link}".ToLowerInvariant();
        }

    }

}
