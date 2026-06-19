namespace TelegramAggregator.WebAPI.Infrastructure // یا هر namespace که دوست داری
{
    public static class StringExtensions
    {
        /// <summary>
        /// اگر متن بیشتر از maxLength بود، آن را کوتاه می‌کند و ... اضافه می‌کند
        /// </summary>
        public static string Truncate(this string? value, int maxLength, string truncationSuffix = "...")
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.Length <= maxLength)
                return value;

            // اگر maxLength خیلی کوچک بود، حداقل ۳ کاراکتر برای ... نگه دار
            if (maxLength < truncationSuffix.Length)
                return value.Substring(0, maxLength);

            return value.Substring(0, maxLength - truncationSuffix.Length) + truncationSuffix;
        }

        public static string? NullIfEmpty(this string? value) =>
            string.IsNullOrEmpty(value) ? null : value;
    }
}