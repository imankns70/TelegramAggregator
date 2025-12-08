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

                // آمار آخرین ساعت — کاملاً واقعی و هماهنگ
                LastHour = new
                {
                    Total = summary.LastHourItemsTotal,
                    Accepted = summary.LastHourAccepted,
                    Rejected = summary.LastHourRejected,
                    AcceptanceRate = summary.LastHourAcceptanceRate
                },

                // آمار امروز — کامل و صادقانه
                Today = new
                {
                    Total = summary.TodayItemsTotal,
                    Accepted = summary.TodayAccepted,
                    Rejected = summary.TodayRejected,
                    AcceptanceRate = summary.TodayAcceptanceRate
                },
              
               summary.DashboardStatusColor
            };

            return Ok(result);
        }


    }
}