namespace TelegramAggregator.WebAPI.Models
{
    /// <summary>
    /// منبع ورود خبر به سیستم (جمع‌آورنده یا گروه ادمین‌ها)
    /// </summary>
    public enum NewsSource
    {
        Unknown = 0,   // حالت پیش‌فرض
        Rss = 1,       // خبرها از فید RSS گرفته می‌شوند
        Telegram = 2,  // خبرها از کانال تلگرام استخراج می‌شوند
        Twitter = 3,   // از توییتر (X) گرفته می‌شوند
        Instagram = 4, // از اینستاگرام
        Website = 5    // از وب‌سایت مستقیم (Scraping)
    }
}
