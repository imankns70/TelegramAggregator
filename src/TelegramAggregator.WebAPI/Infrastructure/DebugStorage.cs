using System.Collections.Concurrent;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public class DebugStorage
    {
        // حداکثر ۲۰۰ تا آخرین خبر رو نگه می‌داره (کافیه برای ۲–۳ ساعت)
        private readonly ConcurrentQueue<DebugFeedItem> _items = new();
        private const int MaxItems = 200;

        public void Add(DebugFeedItem item)
        {
            _items.Enqueue(item);

            // اگر بیشتر از ۲۰۰ تا شد، قدیمی‌ترین حذف بشه
            while (_items.Count > MaxItems)
                _items.TryDequeue(out _);
        }

        public IReadOnlyList<DebugFeedItem> GetRecentItems()
        {
            // جدیدترین اول
            return _items.Reverse().ToList();
        }
    }
}
