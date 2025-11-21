using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public static class NewsBalancer
    {
        public static List<NewsItem> BalanceNews(
            List<NewsItem> items,
            int totalNeeded = 12,
            int minPerSource = 1,
            int maxPerSource = 5,
            ILogger? logger = null)
        {
            if (items == null || !items.Any())
                return new List<NewsItem>();

            // گروه‌بندی بر اساس منبع
            var grouped = items
                .GroupBy(i => i.SourceName)
                .ToDictionary(g => g.Key!, g => g.OrderByDescending(x => x.PubDate).ToList());

            var result = new List<NewsItem>();

            // مرحله ۱: حداقل از هر منبع (minPerSource)
            foreach (var kvp in grouped)
            {
                var sourceItems = kvp.Value;
                result.AddRange(sourceItems.Take(minPerSource));
            }

            logger?.LogInformation("مرحله تضمینی (حداقل {Min} تا از هر منبع): {Count} خبر", minPerSource, result.Count);

            // مرحله ۲: پر کردن تا totalNeeded با رعایت حداکثر
            int added = result.Count;
            if (added < totalNeeded)
            {
                var candidates = items
                    .Where(i => !result.Contains(i)) // اونایی که هنوز انتخاب نشدن
                    .OrderByDescending(i => i.PubDate)
                    .ToList();

                foreach (var item in candidates)
                {
                    if (result.Count >= totalNeeded) break;

                    int currentCount = result.Count(x => x.SourceName == item.SourceName);
                    if (currentCount < maxPerSource)
                    {
                        result.Add(item);
                    }
                }
            }

            // مرحله ۳: مرتب‌سازی نهایی بر اساس زمان
            var final = result
                .OrderByDescending(i => i.PubDate)
                .Take(totalNeeded)
                .ToList();

            // لاگ توزیع — درست و بدون خطا
            if (logger != null)
            {
                var distribution = final
                    .GroupBy(i => i.SourceName)
                    .OrderByDescending(g => g.Count())
                    .Select(g => $"{g.Key}: {g.Count()}");

                logger.LogInformation("توزیع نهایی خبرها: {Distribution} | مجموع: {Total}",
                    string.Join(" | ", distribution),
                    final.Count);
            }

            return final;
        }
    }
}