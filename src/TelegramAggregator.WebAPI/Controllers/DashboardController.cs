using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using TelegramAggregator.WebAPI.Services.Dashboard;

namespace TelegramAggregator.WebAPI.Controllers
{
    [ApiController]
    [Route("api/Dashboard")]
    public class DashboardController : ControllerBase
    {
        private readonly IFootballForiDashboardService _footballforiDashboardService;

        public DashboardController(IFootballForiDashboardService footballforiDashboardService)
        {
            _footballforiDashboardService = footballforiDashboardService;
        }


        [HttpGet("feed")]
        public async Task<IActionResult> Feed(CancellationToken ct)
        { 
            var result = await _footballforiDashboardService.GetDataAsync(ct);
            return Ok(result);
        }


 
      

    }
}