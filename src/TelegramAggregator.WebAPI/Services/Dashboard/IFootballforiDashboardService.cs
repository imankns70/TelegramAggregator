using TelegramAggregator.WebAPI.Models.Dashboard;
using TelegramAggregator.WebAPI.Models.Entities;

namespace TelegramAggregator.WebAPI.Services.Dashboard
{
    public interface IFootballForiDashboardService
    {
        Task<FootbalForiDashboardDto> GetDataAsync(CancellationToken ct);
        Task<(string imageurl, string caption)> GetLastNews(CancellationToken ct);
    }
}
