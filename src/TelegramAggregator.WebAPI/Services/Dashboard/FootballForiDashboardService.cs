using TelegramAggregator.WebAPI.Data.Repositories;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models.Dashboard;

namespace TelegramAggregator.WebAPI.Services.Dashboard
{
    public class FootballForiDashboardService : IFootballForiDashboardService
    {
        private readonly INewsRepository _newsRepository;

        public FootballForiDashboardService(INewsRepository newsRepository)
        {
            _newsRepository = newsRepository;
        }
        public async Task<FootbalForiDashboardDto> GetDataAsync(CancellationToken ct)
        {
            var allDate = await _newsRepository.GetTodayNews(ct);
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
                                  PubDate = item.InsertDate.ToString("HH:mm"),
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
                   SentAt = x.SentDate.Value.ToString("HH:mm:ss")
               }).ToList();

            var todayNewsHistoryData = await _newsRepository.GetTodayNewsHistory(take: 60, ct);


            var recentProcessed = todayNewsHistoryData
                .OrderByDescending(a => a.InsertDate)
                .Select((x, i) => new QueueFootbalForiDashboardDto
            {
                Source = x.News.SportSource.Title,
                Title = x.News.Title?.Truncate(80),
                Link = x.News.Link,
                Status= x.NewsStatus.Title,
                StatusId= x.NewsStatusId,
                Reason= x.Reason,
                InsertTime = x.InsertDate.ToString("HH:mm:ss"),

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

    }
}
