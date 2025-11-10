using TelegramAggregator.WebAPI.Models.TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Core
{
    /// <summary>
    /// مدیریت مجموعه‌ای از اخبار اخیر (مثلاً ۱۰۰۰ خبر آخر)
    /// و نگهداری صف انتشار
    /// </summary>
    public class NewsStorage
    {
        private readonly int _maxItems;
        private readonly LinkedList<NewsItem> _items = new();
        private readonly HashSet<string> _keySet = new();

        /// <summary>
        /// صف آماده برای انتشار
        /// </summary>
        public Queue<NewsItem> Queue { get; } = new();

        public NewsStorage(int maxItems = 1000)
        {
            _maxItems = maxItems;
        }

        /// <summary>
        /// اضافه کردن خبر به ذخیره
        /// </summary>
        public void Add(NewsItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Link)) return;

            var key = $"{item.Link}-{item.PubDate:yyyyMMddHHmm}";
            if (_keySet.Contains(key)) return;

            _items.AddLast(item);
            _keySet.Add(key);
            Queue.Enqueue(item);

            // حذف قدیمی‌ها اگر تعداد از حد مجاز بیشتر شد
            if (_items.Count > _maxItems)
            {
                var oldest = _items.First!;
                _items.RemoveFirst();
                _keySet.Remove($"{oldest.Value.Link}-{oldest.Value.PubDate:yyyyMMddHHmm}");
            }
        }

        /// <summary>
        /// دسترسی به اخبار اخیر (برای DuplicateChecker)
        /// </summary>
        public IReadOnlyCollection<NewsItem> GetRecentItems() => _items;

        /// <summary>
        /// پاک کردن همه‌ی اخبار و صف انتشار
        /// </summary>
        public void Clear()
        {
            _items.Clear();
            _keySet.Clear();
            Queue.Clear();
        }

        /// <summary>
        /// تعداد اخبار ذخیره‌شده
        /// </summary>
        public int Count => _items.Count;
    }
}
