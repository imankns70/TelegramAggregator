using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public static class NewsBalancer
    {
        /// <summary>
        /// انتخاب عادلانه خبرها با رعایت حداقل و حداکثر از هر منبع
        /// </summary>
        /// <param name="items">لیست همه خبرهای فیلتر شده</param>
        /// <param name="totalNeeded">تعداد خبر نهایی (مثلاً 10)</param>
        /// <param name="minPerSource">حداقل از هر منبع (مثلاً 2)</param>
        /// <param name="maxPerSource">حداکثر از هر منبع (مثلاً 5)</param>
        /// <param name="logger">اختیاری: برای لاگ کردن نتیجه هر منبع</param>
        /// <returns>لیست نهایی متعادل</returns>
        public static List<NewsItem> BalanceNews(
            List<NewsItem> items,
            int totalNeeded = 10,
            int minPerSource = 2,
            int maxPerSource = 5,
            ILogger? logger = null)
        {
            if (items == null || !items.Any())
                return new List<NewsItem>();

            // مرحله ۱: حداقل تعداد از هر منبع (جدیدترین‌ها)
            var guaranteed = items
                .GroupBy(i => i.SourceName) // ← حتماً Source یا SourceName داشته باشه
                .SelectMany(g => g.OrderByDescending(x => x.PubDate).Take(minPerSource))
                .ToList();

            logger?.LogInformation("مرحله تضمینی: {Count} خبر از {Sources} منبع",
                guaranteed.Count, guaranteed.Select(x => x.SourceName).Distinct().Count());

            // مرحله ۲: محاسبه جای خالی
            int remaining = totalNeeded - guaranteed.Count;
            if (remaining <= 0)
            {
                var result = guaranteed.OrderByDescending(i => i.PubDate).Take(totalNeeded).ToList();
                LogDistribution(result, logger);
                return result;
            }

            // مرحله ۳: خبرهای اضافی با رعایت حداکثر
            var additional = items
                .Except(guaranteed) // اونایی که هنوز انتخاب نشدن
                .GroupBy(i => i.SourceName)
                .SelectMany(g =>
                {
                    int alreadyTaken = guaranteed.Count(x => x.SourceName == g.Key);
                    int canStillTake = maxPerSource - alreadyTaken;
                    return canStillTake > 0
                        ? g.OrderByDescending(x => x.PubDate).Take(canStillTake)
                        : Enumerable.Empty<NewsItem>();
                })
                .OrderByDescending(i => i.PubDate)
                .Take(remaining)
                .ToList();

            // مرحله ۴: ترکیب نهایی
            var finalList = guaranteed
                .Concat(additional)
                .OrderByDescending(i => i.PubDate)
                .Take(totalNeeded)
                .ToList();

            LogDistribution(finalList, logger);

            return finalList;
        }

        // لاگ توزیع نهایی (اختیاری ولی خیلی مفیده)
        private static void LogDistribution(List<NewsItem> items, ILogger? logger)
        {
            if (logger == null) return;

            var distribution = items
                .GroupBy(i => i.SourceName)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key}: {g.Count()} تا");

            logger?.LogInformation("توزیع نهایی خبرها: {Distribution} | مجموع: {Total}",
                string.Join(" | ", distribution), items.Count);
        }
    }
}
