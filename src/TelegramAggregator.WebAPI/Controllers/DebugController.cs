using Microsoft.AspNetCore.Mvc;
using TelegramAggregator.WebAPI.Services.Dashboard;

namespace TelegramAggregator.WebAPI.Controllers
{
    [ApiController]
    [Route("api/debug")]
    public class DebugApiController : ControllerBase
    { 
        private readonly IFootballForiDashboardService _footballforiDashboardService;

        public DebugApiController(IFootballForiDashboardService footballforiDashboardService)
        {
            _footballforiDashboardService = footballforiDashboardService;
        }

        /// <summary>
        /// صفحه اصلی دیباگ — داشبورد زنده با صف‌های کامل و شفاف
        /// </summary>
        [HttpGet("feed")]
        public IActionResult Feed()
        {
            var result = _footballforiDashboardService.GetFeed();
            return Ok(result);
        }

        /// <summary>
        /// آمار لحظه‌ای برای داشبورد — سریع، هوشمند و کاملاً استاندارد
        /// </summary>
        [HttpGet("stats")]
        public IActionResult Stats()
        { 
            var summary = _footballforiDashboardService.GetSummary();

            var result = new
            {
                // آمار کلی — ۱۰۰٪ صادقانه (از GetSummary که درست کردیم)
                summary.TotalProcessed,
                summary.AcceptedCount,
                summary.RejectedCount,
                summary.PublishedCount,
                summary.InQueueCount,
                summary.AcceptanceRate,
                summary.SystemStatus,
                summary.StatusColor,
                summary.QueueDetails,
                summary.DebugItems,
                summary.SeenKeys,

                
            };

            return Ok(result);
        }


    }
}