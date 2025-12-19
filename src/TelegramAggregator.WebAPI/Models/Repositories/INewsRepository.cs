using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Models.Entities;
using TelegramAggregator.WebAPI.Models.FootbalSettings;

namespace TelegramAggregator.WebAPI.Data.Repositories
{
    public interface INewsRepository
    {
        IQueryable<News> GetAll();
        Task<News> GetByIdAsync(long id, CancellationToken ct);
        Task ReceiveAsync(NewsItem newsItem, CancellationToken ct);
        Task PublishedAsync(News newsItem, CancellationToken ct);
        Task DecayAsync(News news, string reason, CancellationToken ct, bool saveNow = false);
        void FilterNews(List<NewsItem> news);
        Task ReceiveRangeAsync(List<NewsItem> newsItems, CancellationToken ct);
        Task ProcessNewsAsync(List<NewsItem> newsItems, FootbalForiSettings footbalForiSettings, CancellationToken ct);
        Task<News> GetBestNews(CancellationToken ct);
        Task ChangeNewsInQueueToExpiredState(CancellationToken ct);
        Task<IReadOnlyList<News>> GetTodayNews(CancellationToken ct);
        Task<IReadOnlyList<NewsHistory>> GetTodayNewsHistory(int take, CancellationToken ct);
    }
}
