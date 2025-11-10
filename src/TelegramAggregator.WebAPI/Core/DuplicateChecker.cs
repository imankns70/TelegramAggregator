using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models.TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Core
{
    /// <summary>
    /// بررسی می‌کند که یک خبر جدید تکراری یا قدیمی است یا خیر
    /// بر اساس اخبار موجود در NewsStorage
    /// </summary>
    public class DuplicateChecker
    {
        private readonly double _similarityThreshold;
        private readonly NewsStorage _storage;

        public DuplicateChecker(NewsStorage storage, double similarityThreshold = 0.88)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _similarityThreshold = similarityThreshold;
        }

        /// <summary>
        /// بررسی می‌کند آیا خبر تکراری یا قدیمی است
        /// </summary>
        public bool IsDuplicateOrOld(NewsItem item)
        {
            if (item == null) return true;

            foreach (var oldItem in _storage.GetRecentItems())
            {
                // 1️⃣ بررسی قدیمی بودن خبر
                if (item.PubDate <= oldItem.PubDate)
                    return true;

                // 2️⃣ بررسی لینک دقیق
                if (!string.IsNullOrWhiteSpace(item.Link) && item.Link == oldItem.Link)
                    return true;

                // 3️⃣ بررسی شباهت عنوان
                double titleSim = TextSimilarity.GetSimilarity(item.Title, oldItem.Title);
                if (titleSim >= _similarityThreshold)
                    return true;

                // 4️⃣ بررسی شباهت توضیحات
                if (!string.IsNullOrEmpty(item.FullDescription) && !string.IsNullOrEmpty(oldItem.FullDescription))
                {
                    double descSim = TextSimilarity.GetSimilarity(item.FullDescription, oldItem.FullDescription);
                    if (descSim >= _similarityThreshold)
                        return true;
                }
            }

            return false;
        }
    }
}
