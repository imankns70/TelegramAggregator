using TelegramAggregator.WebAPI.Models.Entities;

namespace TelegramAggregator.WebAPI.Data.Repositories
{
    public interface INewsRepository
    {
        IQueryable<News> GetAllAsync();
        Task<News> GetByIdAsync(int id, CancellationToken ct);

    }
}
