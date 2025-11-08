namespace TelegramAggregator.WebAPI.Models
{
    /// <summary>
    /// منبع ورود خبر به سیستم (جمع‌آورنده یا گروه ادمین‌ها)
    /// </summary>
    public enum NewsSource
    {
        /// <summary>
        /// خبر از سیستم جمع‌آورنده RSS یا خزنده آمده است
        /// </summary>
        Aggregator = 0,

        /// <summary>
        /// خبر از گروه ادمین‌ها (در تلگرام) ارسال شده است
        /// </summary>
        AdminGroup = 1
    }
}
