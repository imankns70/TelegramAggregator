using System.Collections.Concurrent;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Infrastructure
{
    public interface IDebugStore
    {
     
        IReadOnlyList<DebugFeedItem> GetAllDebugItems();
        IReadOnlyList<DebugFeedItem> GetPublishedItems();
        IReadOnlyList<DebugFeedItem> GetAcceptedItems();
        IReadOnlyList<DebugFeedItem> GetRecentProcessed(int take = 40);
        void LogOrUpdate(DebugFeedItem item);
        void LogPublished(NewsItem item);
        void LogRejected(NewsItem item, string reason);
        void LogAccepted(NewsItem item);
    }
}
