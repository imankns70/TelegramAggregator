using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Models.Entities;

namespace TelegramAggregator.WebAPI.Data.Repositories
{
    public interface INewsRepository
    {
        IQueryable<News> GetAll();
        Task<News> GetByIdAsync(long id, CancellationToken ct);
        Task AcceptedAsync(NewsItem newsItem, CancellationToken ct);
        Task PublishedAsync(NewsItem newsItem, CancellationToken ct);
        Task DecayAsync(NewsItem newsItem, string reason, CancellationToken ct);

    }
}
