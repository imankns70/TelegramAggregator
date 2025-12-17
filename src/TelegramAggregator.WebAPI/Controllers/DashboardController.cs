using Microsoft.AspNetCore.Mvc;
using TelegramAggregator.WebAPI.Services.Dashboard;

namespace TelegramAggregator.WebAPI.Controllers
{
    [ApiController]
    [Route("api/debug")]
    public class DashboardController : ControllerBase
    { 
        private readonly IFootballForiDashboardService _footballforiDashboardService;

        public DashboardController(IFootballForiDashboardService footballforiDashboardService)
        {
            _footballforiDashboardService = footballforiDashboardService;
        }

        
        [HttpGet("feed")]
        public IActionResult Feed(CancellationToken ct)
        {
            var result = _footballforiDashboardService.GetDataAsync(ct);
            return Ok(result);
        }

 


    }
}