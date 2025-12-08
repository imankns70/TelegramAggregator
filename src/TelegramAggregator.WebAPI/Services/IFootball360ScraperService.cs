using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Services
{
    public interface IFootball360ScraperService
    {
        Task<List<NewsItem>> GetLatestNewsAsync(CancellationToken ct = default);
    }
}
