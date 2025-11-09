using TelegramAggregator.WebAPI.Models.TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Core
{
    public class DuplicateChecker
    {
        private readonly int _maxItems;
        private readonly LinkedList<string> _recentKeys;
        private readonly HashSet<string> _keySet;
        private DateTime _latestPubDate = DateTime.MinValue;

        public DuplicateChecker(int maxItems)
        {
            _maxItems = maxItems;
            _recentKeys = new LinkedList<string>();
            _keySet = new HashSet<string>();
        }

        public bool IsDuplicateOrOld(NewsItem item)
        {
            if (item.PubDate <= _latestPubDate)
                return true;

            var key = $"{item.UniqueKey}-{item.PubDate:yyyyMMddHHmm}";

            if (_keySet.Contains(key))
                return true;

            _recentKeys.AddLast(key);
            _keySet.Add(key);

            if (item.PubDate > _latestPubDate)
                _latestPubDate = item.PubDate;

            if (_recentKeys.Count > _maxItems)
            {
                var oldestNode = _recentKeys.First;
                _recentKeys.RemoveFirst();
                _keySet.Remove(oldestNode!.Value);
            }

            return false;
        }
    }
}
