using TelegramAggregator.WebAPI.Models.Dashboard;

namespace TelegramAggregator.WebAPI.Services.Dashboard
{
    public interface IFootballForiDashboardService
    {
        Task<FootbalForiDashboardDto> GetDataAsync(CancellationToken ct);
    }
}
