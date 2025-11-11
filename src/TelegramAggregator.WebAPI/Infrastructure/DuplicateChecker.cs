

using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure
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
            if (item == null)
                return true;

            foreach (var oldItem in _storage.GetRecentItems())
            {
                // ✅ 1️⃣ اگر تاریخ انتشار یکی باشد (مثلاً هر دو 11:32 باشند) → تکراری محسوب شود
                if (item.PubDate == oldItem.PubDate)
                    return true;

                // ✅ 2️⃣ اگر زمان انتشار خبر جدید قبل از خبر قدیمی است → قدیمی محسوب می‌شود
                if (item.PubDate <= oldItem.PubDate)
                    return true;

                // ✅ 3️⃣ بررسی شباهت عنوان
                double titleSim = TextSimilarity.GetSimilarity(item.Title, oldItem.Title);
                if (titleSim >= _similarityThreshold)
                    return true;

                // ✅ 4️⃣ بررسی شباهت توضیحات (Full و Short)
                string fullDescA = item.FullDescription ?? string.Empty;
                string fullDescB = oldItem.FullDescription ?? string.Empty;
                string shortDescA = item.ShortDescription ?? string.Empty;
                string shortDescB = oldItem.ShortDescription ?? string.Empty;

                string combinedA = $"{item.Title} {shortDescA} {fullDescA}";
                string combinedB = $"{oldItem.Title} {shortDescB} {fullDescB}";

                double contentSim = TextSimilarity.GetSimilarity(combinedA, combinedB);
                if (contentSim >= _similarityThreshold)
                    return true;

                // ✅ 5️⃣ بررسی لینک فقط در صورتی که مقدار داشته باشد
                if (!string.IsNullOrWhiteSpace(item.Link) &&
                    !string.IsNullOrWhiteSpace(oldItem.Link) &&
                    item.Link == oldItem.Link)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
