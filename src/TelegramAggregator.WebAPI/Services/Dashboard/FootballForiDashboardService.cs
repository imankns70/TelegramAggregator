using System;
using TelegramAggregator.WebAPI.Data.Repositories;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models.Dashboard;
using TelegramAggregator.WebAPI.Models.Entities;

namespace TelegramAggregator.WebAPI.Services.Dashboard
{
    public class FootballForiDashboardService : IFootballForiDashboardService
    {
        private readonly INewsRepository _newsRepository;

        public FootballForiDashboardService(INewsRepository newsRepository)
        {
            _newsRepository = newsRepository;
        }
        public static DateTimeOffset GetTodayStartUtcOffset(int hour = 8)
        {
            var iranTimeZone = GetIranTimeZone();

            // زمان فعلی ایران به صورت DateTimeOffset
            var nowIran = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, iranTimeZone);

            // امروز ایران ساعت 00:00
            var todayIran = new DateTimeOffset(
                nowIran.Year,
                nowIran.Month,
                nowIran.Day,
                0, 0, 0,
                iranTimeZone.GetUtcOffset(nowIran));

            // ساعت شروع بیزینس (مثلاً ۸ صبح ایران)
            var startIran = todayIran.AddHours(hour);

            // تبدیل به UTC
            return startIran.ToUniversalTime();
        }
        public async Task<FootbalForiDashboardDto> GetDataAsync(CancellationToken ct)
        {
            var iranStartOfDay = GetTodayStartUtcOffset(hour: 8);

            var iranTimeZone = GetIranTimeZone();
            var allDate = await _newsRepository.GetTodayNews(iranStartOfDay, ct);
            var accepted = allDate.Count(a => a.NewsStatusId == StatusEnumeration.Receive.Id || a.NewsStatusId == StatusEnumeration.InQueue.Id ||
                       a.NewsStatusId == StatusEnumeration.Sent.Id);

            var rejected = allDate.Count(a => a.NewsStatusId == StatusEnumeration.Decay.Id);
            var published = allDate.Count(a => a.NewsStatusId == StatusEnumeration.Sent.Id);
            var inQueue = allDate.Count(a => a.NewsStatusId == StatusEnumeration.InQueue.Id);

            var acceptanceRate = allDate.Count > 0 ? Math.Round((double)accepted / allDate.Count * 100, 1) : 0;

            var statusColor = inQueue > 50 ? "red" :
                              inQueue > 20 ? "yellow" :
                              acceptanceRate < 80 ? "yellow" : "green";

            var queues = allDate
                      .Where(a => a.NewsStatusId == StatusEnumeration.InQueue.Id)
                      .GroupBy(a => a.SportSource.Title)
                      .ToDictionary(
                          g => g.Key,
                          g => g
                              .OrderByDescending(x => x.Score)
                              .Select((item, index) => new QueueFootbalForiDashboardDto
                              {
                                  Score = item.Score,
                                  Position = index + 1,
                                  Title = item.Title?.Truncate(90),
                                  Link = item.Link,
                                  PubDate = TimeZoneInfo.ConvertTime(item.InsertDate, iranTimeZone).ToString("HH:mm"),
                                  Source = item.SportSource.Title
                              })
                              .ToList()
                      );

            var publishedItems = allDate.Where(a => a.NewsStatusId == StatusEnumeration.Sent.Id)
               .OrderByDescending(a => a.SentDate)
               .Select((x, i) => new QueueFootbalForiDashboardDto
               {
                   Score = x.Score,
                   Position = i + 1,
                   Source = x.SportSource.Title,

                   Title = x.Title?.Truncate(80),
                   Link = x.Link,
                   SentAt = TimeZoneInfo.ConvertTime(x.SentDate.Value, iranTimeZone).ToString("HH:mm"),

               }).ToList();

            var todayNewsHistoryData = await _newsRepository.GetTodayNewsHistory(iranStartOfDay, ct);


            var recentProcessed = todayNewsHistoryData
                .OrderByDescending(a => a.InsertDate)
                .Select((x, i) => new QueueFootbalForiDashboardDto
                {
                    Source = x.News.SportSource.Title,
                    Title = x.News.Title?.Truncate(80),
                    Link = x.News.Link,
                    Status = x.NewsStatus.Title,
                    StatusId = x.NewsStatusId,
                    Reason = x.Reason,
                    InsertTime = TimeZoneInfo.ConvertTime(x.InsertDate, iranTimeZone).ToString("HH:mm"),

                }).ToList();

            var viewModel = new FootbalForiDashboardDto
            {
                TotalProcessed = allDate.Count,
                AcceptedCount = accepted,
                RejectedCount = rejected,
                PublishedCount = published,
                InQueueCount = inQueue,
                AcceptanceRate = acceptanceRate,
                SystemStatus = acceptanceRate >= 90 ? "عالی" : "خوب",
                StatusColor = statusColor,
                Queues = queues,
                PublishedItems = publishedItems,
                RecentProcessed = recentProcessed,

            };

            return viewModel;
        }

        public async Task<(string imageurl, string caption)> GetLastNews(CancellationToken ct)
        {
            var item = await _newsRepository.GetLastNews(ct);
            if (item != null)
            {
                var caption = CaptionMakerExtension.GetCaption(item);
                return (imageurl: item.ImageUrl, caption: caption);
            }

            return (imageurl: "", caption: "");
        }

        private static TimeZoneInfo GetIranTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
            }
        }
    }
}
