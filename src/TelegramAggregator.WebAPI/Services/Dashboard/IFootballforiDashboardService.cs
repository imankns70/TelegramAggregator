using TelegramAggregator.WebAPI.Models;
 
namespace TelegramAggregator.WebAPI.Services.Dashboard
{
    public interface IFootballForiDashboardService
    {
        object GetFeed();
        DebugSummary GetSummary();
    }
}
