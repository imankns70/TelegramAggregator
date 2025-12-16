using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Models.Entities;
using TelegramAggregator.WebAPI.Models.FootbalSettings;

namespace TelegramAggregator.WebAPI.Data.Repositories
{
    public interface INewsRepository
    {
        IQueryable<News> GetAll();
        Task<News> GetByIdAsync(long id, CancellationToken ct);
        Task<long> ReceiveAsync(NewsItem newsItem, CancellationToken ct);
        Task AcceptedAsync(NewsItem newsItem, CancellationToken ct);
        Task PublishedAsync(NewsItem newsItem, CancellationToken ct);
        Task DecayAsync(NewsItem newsItem, string reason, CancellationToken ct);
        void FilterNews(List<NewsItem> news);
        Task ReceiveRangeAsync(List<NewsItem> newsItems, CancellationToken ct);
        Task ProcessNewsToInQueueOrDecay(List<NewsItem> newsItems, FootbalForiSettings footbalForiSettings, CancellationToken ct);

    }
}
