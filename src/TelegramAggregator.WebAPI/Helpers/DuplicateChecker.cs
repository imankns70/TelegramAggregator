using TelegramAggregator.WebAPI.Models.TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Helpers
{
    public class DuplicateChecker
    {
        private readonly int _maxItems;
        private readonly LinkedList<string> _recentKeys; // نگهداری ترتیب ورود
        private readonly HashSet<string> _keySet;        // چک سریع وجود
        private DateTime _latestPubDate = DateTime.MinValue; // تاریخ آخرین خبر فرستاده

        public DuplicateChecker(int maxItems)
        {
            _maxItems = maxItems;
            _recentKeys = new LinkedList<string>();
            _keySet = new HashSet<string>();
        }

        /// <summary>
        /// بررسی می‌کند آیا خبر تکراری یا قدیمی است یا نه.
        /// اگر خبر جدید باشد، اضافه می‌کند.
        /// </summary>
        public bool IsDuplicateOrOld(NewsItem item)
        {
            // 1. بررسی تاریخ انتشار: اگر خبر قدیمی‌تر یا برابر آخرین خبر فرستاده باشد → تکراری/قدیمی
            if (item.PubDate <= _latestPubDate)
                return true;

            var key = GenerateKey(item);

            // 2. بررسی کلید یکتا
            if (_keySet.Contains(key))
                return true;

            // 3. خبر جدید و یکتا، اضافه کن
            AddKey(key, item.PubDate);
            return false;
        }

        private string GenerateKey(NewsItem item)
        {
            // ترکیب UniqueKey و تاریخ انتشار برای جلوگیری از تکراری‌ها
            return $"{item.UniqueKey}-{item.PubDate:yyyyMMddHHmm}";
        }

        private void AddKey(string key, DateTime pubDate)
        {
            _recentKeys.AddLast(key);
            _keySet.Add(key);

            // آپدیت آخرین تاریخ خبر فرستاده شده
            if (pubDate > _latestPubDate)
                _latestPubDate = pubDate;

            // محدودیت 1000 تایی
            if (_recentKeys.Count > _maxItems)
            {
                var oldestNode = _recentKeys.First;
                if (oldestNode != null)
                {
                    _recentKeys.RemoveFirst();
                    _keySet.Remove(oldestNode.Value);
                }
            }
        }
    }
}
