using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure.Storage
{
    public interface INewsStorage
    {
        bool TryDequeueAny(out NewsItem item);
        Task<(bool, string)> TryAddAsync(NewsItem item, CancellationToken ct = default);
        IReadOnlyDictionary<string, List<DebugFeedItem>> GetMirroQueue();
        IReadOnlyDictionary<string, int> GetChannelQueueCounts();
        int GetSeenKeys();
        void ClearAllQueues();
    }
}
