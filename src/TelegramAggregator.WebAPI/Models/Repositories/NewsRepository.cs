using Microsoft.EntityFrameworkCore;
using TelegramAggregator.WebAPI.Data;
using TelegramAggregator.WebAPI.Data.Repositories;
using TelegramAggregator.WebAPI.Models.Entities;

namespace TelegramAggregator.WebAPI.Models.Repositories
{
    public class NewsRepository : INewsRepository
    {
        private readonly TelegramAggregatorContext _context;

        public NewsRepository(TelegramAggregatorContext context)
        {
            _context = context;
        }

        public IQueryable<News> GetAllAsync()
        {
            return _context.News.AsQueryable();
        }

        public async Task<News> GetByIdAsync(int id, CancellationToken ct)
        {
            return await _context.News.FirstOrDefaultAsync(ct);
        }

    }
}
