using Microsoft.EntityFrameworkCore;
using System.Linq;
using TelegramAggregator.WebAPI.Data;
using TelegramAggregator.WebAPI.Data.Repositories;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models.Entities;
using TelegramAggregator.WebAPI.Models.FootbalSettings;
using TelegramAggregator.WebAPI.Services;

namespace TelegramAggregator.WebAPI.Models.Repositories
{
    public class NewsRepository : INewsRepository
    {
        private readonly TelegramAggregatorContext _context;
        private readonly ILogger<NewsRepository> _logger;
        public NewsRepository(TelegramAggregatorContext context, ILogger<NewsRepository> logger)
        {
            _context = context;
            _logger = logger;
        }

        public IQueryable<News> GetAll()
        {
            return _context.News.AsQueryable();
        }

        public async Task<News> GetByIdAsync(long id, CancellationToken ct)
        {
            return await GetAll()
            .Include(a => a.Histories).FirstOrDefaultAsync(a => a.Id == id, ct);
        }

        public async Task InQueueAsync(NewsItem newsItem, FootbalForiSettings footbalForiSettings, CancellationToken ct)
        {
            var news = await GetAll().FirstOrDefaultAsync(a => a.Id == newsItem.Id, ct);
            if (news != null)
            {
                news.Score = ScoreCalculator.Calculate(newsItem, footbalForiSettings);
                news.NewsStatusId = StatusEnumeration.InQueue.Id;
                news.Histories.Add(new NewsHistory
                {
                    NewsStatusId = StatusEnumeration.InQueue.Id,
                    InsertDate = DateTimeOffset.UtcNow
                });
            }
            else
            {
                _logger.LogWarning("خبر با لینک {Link} برای تغییر وضعیت به صف یافت نشد", newsItem.Link);
            }
        }
        public async Task InQueueRangeAsync(List<NewsItem> newsItems, FootbalForiSettings footbalForiSettings, CancellationToken ct)
        {
            // Bulk-load all IDs in one query instead of N+1 queries
            var ids = newsItems.Select(n => n.Id).ToHashSet();
            var newsInDb = await GetAll()
                .Include(n => n.Histories)
                .Where(n => ids.Contains(n.Id))
                .ToListAsync(ct);

            var dbLookup = newsInDb.ToDictionary(n => n.Id);
            foreach (var newsItem in newsItems)
            {
                if (dbLookup.TryGetValue(newsItem.Id.GetValueOrDefault(), out var news))
                {
                    news.Score = ScoreCalculator.Calculate(newsItem, footbalForiSettings);
                    news.NewsStatusId = StatusEnumeration.InQueue.Id;
                    news.Histories.Add(new NewsHistory
                    {
                        NewsStatusId = StatusEnumeration.InQueue.Id,
                        InsertDate = DateTimeOffset.UtcNow
                    });
                }
                else
                {
                    _logger.LogWarning("خبر با لینک {Link} برای تغییر وضعیت به صف یافت نشد", newsItem.Link);
                }
            }
            await _context.SaveChangesAsync(ct);
        }
        public async Task DecayAsync(News news, string reason, CancellationToken ct, bool saveNow = false)
        {

            news.NewsStatusId = StatusEnumeration.Decay.Id;
            news.Histories.Add(new NewsHistory

            {
                NewsStatusId = StatusEnumeration.Decay.Id,
                InsertDate = DateTimeOffset.UtcNow,
                Reason = reason
            });


            if (saveNow)
            {
                await _context.SaveChangesAsync(ct);
            }
        }
        public async Task DecayRangeAsync(List<(string Reason, NewsItem Item)> newsItems, CancellationToken ct)
        {
            var ids = newsItems.Select(a => a.Item.Id).ToHashSet();
            var newsInDb = await GetAll()
                .Include(n => n.Histories)
                .Where(a => ids.Contains(a.Id))
                .ToListAsync(ct);

            var dbLookup = newsInDb.ToDictionary(n => n.Id);
            foreach (var item in newsItems)
            {
                if (dbLookup.TryGetValue(item.Item.Id.GetValueOrDefault(), out var news))
                    await DecayAsync(news, item.Reason, ct);
            }
            await _context.SaveChangesAsync(ct);  // was missing CancellationToken
        }


        public async Task ProcessNewsAsync(List<NewsItem> newsItems, FootbalForiSettings footbalForiSettings, CancellationToken ct)
        {

            newsItems = newsItems
                .GroupBy(item => ClearUrl(item.Link))
                .Select(group => group.First())
                .ToList();
            await ReceiveRangeAsync(newsItems, ct);
            newsItems = newsItems.Where(item => item.Id.HasValue).ToList();
            var videoKeywords = footbalForiSettings.VideoKeywords;
            var otherSportsKeywords = footbalForiSettings.OtherSportsKeywords;
            var womenKeywords = footbalForiSettings.WomenKeywords;
            //var cutoff = DateTimeOffset.Now.AddHours(-6);

            // استفاده از List برای نگه داشتن decay با دلیل
            List<(string Reason, NewsItem Item)> decayList = new List<(string, NewsItem)>();
            List<NewsItem> inQueueList = new List<NewsItem>();

            var eightHoursAgo = DateTimeOffset.UtcNow.AddHours(-8);
            var dbCandidates = await GetAll()
                .Where(n => n.InsertDate >= eightHoursAgo)
                .AsNoTracking()
                .ToListAsync(ct);

            foreach (var item in newsItems)
            {
                var text = $"{item.Title} {item.ShortDescription}".ToLowerInvariant();
                bool isDecay = false;

                int sportSourceId = NewsUrl.GetSourceId(item.Link);


                string decayReason = otherSportsKeywords
                    .FirstOrDefault(k => text.Contains(k.ToLowerInvariant()));

                if (!string.IsNullOrWhiteSpace(decayReason))
                {
                    decayList.Add((decayReason, item));
                    continue;
                }
                else if (videoKeywords.Any(k => text.Contains(k.ToLowerInvariant())))
                {
                    decayList.Add(("عنوان ویدئو", item));
                    continue;
                }
                else if (womenKeywords.Any(k => text.Contains(k.ToLowerInvariant())))
                {
                    decayList.Add(("فوتبال بانوان", item));
                    continue;
                }
                else if (text.Contains("ترکیب رسمی")) decayList.Add(("ترکیب رسمی", item));
                else if (text.Contains("برنده های پیش بینی")) decayList.Add(("برنده های پیش بینی", item));
                else if (text.Contains("جدول پخش زنده")) decayList.Add(("جدول پخش زنده", item));
                else if (text.Contains("پخش بازی")) decayList.Add(("پخش بازی", item));
                else if (text.Contains("روزنامه خبرورزشی")) decayList.Add(("روزنامه خبرورزشی", item));
                //else if (item.PubDate < cutoff) decayList.Add(("قدیمی‌تر از ۶ ساعت", item));

                if (decayList.Any(d => d.Item == item))
                    continue;


                foreach (var dbItem in dbCandidates)
                {
                    if (dbItem.SportSourceId == sportSourceId)
                        continue;

                    var a = $"{item.Title} {item.ShortDescription} {item.FullDescription}".Trim();
                    var b = $"{dbItem.Title} {dbItem.ShortDescription} {dbItem.FullDescription}".Trim();

                    if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
                        TextSimilarity.GetSimilarity(a, b) >= 0.90)
                    {
                        decayList.Add(("خبر منبع دیگر", item));
                        isDecay = true;
                        break;
                    }
                }

                if (!isDecay)
                {
                    inQueueList.Add(item);
                }

            }
            if (decayList.Any())
            {
                await DecayRangeAsync(decayList, ct);
            }

            if (inQueueList.Any())
            {
                await InQueueRangeAsync(inQueueList, footbalForiSettings, ct);
            }
        }
        public async Task<News> GetBestNews(CancellationToken ct)
        {
            var item = await GetAll()
                .Include(n => n.Histories)
                .Where(n => n.NewsStatusId == StatusEnumeration.InQueue.Id)
                .OrderByDescending(n => n.Score)
                .ThenByDescending(n => n.PubDate)  // newest first among equal scores
                .FirstOrDefaultAsync(ct);

            return item;
        }

        public async Task PublishedAsync(News news, CancellationToken ct)
        {
            var now = DateTimeOffset.UtcNow;

            news.NewsStatusId = StatusEnumeration.Sent.Id;
            news.SentDate = now;
            news.Histories.Add(new NewsHistory
            {
                NewsStatusId = StatusEnumeration.Sent.Id,
                InsertDate = now
            });

            await _context.SaveChangesAsync(ct);
        }



        public void FilterNews(List<NewsItem> news)
        {
            if (!news.Any())
                return;

            int sportSourceId = NewsUrl.GetSourceId(news.First().Link);

            // 1. Clean incoming links
            var cleanedNews = news
                .Select(n => new
                {
                    Item = n,
                    CleanLink = ClearUrl(n.Link)
                })
                .ToList();

            HashSet<string> links = cleanedNews
                .Select(x => x.CleanLink)
                .ToHashSet();


            HashSet<string> existLinks =
                _context.News
                    .Where(a => a.SportSourceId == sportSourceId &&
                                links.Contains(a.Link))
                    .Select(a => a.Link)
                    .ToHashSet();

            // 3. Keep only NEW news
            news.Clear();
            news.AddRange(
                cleanedNews
                    .Where(x => !existLinks.Contains(x.CleanLink))
                    .Select(x => x.Item)
            );
        }

        public async Task ReceiveRangeAsync(List<NewsItem> newsItems, CancellationToken ct)
        {
            // Skip items whose links are already in the DB (dedup before insert)
            var cleanLinks = newsItems.Select(i => ClearUrl(i.Link)).ToHashSet();
            var existingLinks = await _context.News
                .Where(n => cleanLinks.Contains(n.Link))
                .Select(n => n.Link)
                .ToHashSetAsync(ct);

            var newEntities = new List<(News Entity, NewsItem Item)>();
            var now = DateTimeOffset.UtcNow;

            foreach (var item in newsItems)
            {
                item.Id = null;
                string cleanLink = ClearUrl(item.Link);
                if (!existingLinks.Add(cleanLink))
                    continue;

                int sportSourceId = NewsUrl.GetSourceId(item.Link);
                var news = new News
                {
                    Link = cleanLink,
                    Title = item.Title,
                    ShortDescription = item.ShortDescription,
                    FullDescription = item.FullDescription,
                    InsertDate = now,
                    PubDate = item.PubDate.ToUniversalTime(),
                    SportSourceId = sportSourceId,
                    NewsStatusId = StatusEnumeration.Receive.Id,
                };

                news.Histories.Add(new NewsHistory
                {
                    InsertDate = now,
                    NewsStatusId = StatusEnumeration.Receive.Id,
                });

                _context.News.Add(news);
                newEntities.Add((news, item));
            }

            // Single SaveChanges for all inserts (was N separate saves)
            await _context.SaveChangesAsync(ct);

            // Assign generated IDs back to NewsItems
            foreach (var (entity, item) in newEntities)
                item.Id = entity.Id;
        }


        public async Task ChangeNewsInQueueToExpiredState(CancellationToken ct)
        {
            // Expire news inserted BEFORE today (i.e., yesterday and older).
            // Bug was: >= yest.Date which also expired today's news.
            var todayUtc = DateTimeOffset.UtcNow.Date;
            var inQueueNews = await GetAll()
                                         .Include(n => n.Histories)
                                         .Where(n => (n.NewsStatusId == StatusEnumeration.InQueue.Id
                                             || n.NewsStatusId == StatusEnumeration.Receive.Id)
                                         && n.InsertDate.Date < todayUtc)
                                         .ToListAsync(ct);
            if (!inQueueNews.Any())
                return;

            foreach (var news in inQueueNews)
            {
                news.NewsStatusId = StatusEnumeration.Expired.Id;
                news.Histories.Add(new NewsHistory
                {
                    NewsStatusId = StatusEnumeration.Expired.Id,
                    InsertDate = DateTimeOffset.UtcNow
                });
            }

            await _context.SaveChangesAsync(ct);

        }


        private string ClearUrl(string url)
        {
            return NewsUrl.Normalize(url);
        }

        public async Task<IReadOnlyList<News>> GetTodayNews(DateTimeOffset iranStartDate, CancellationToken ct)
        {


            var allDate = await GetAll().Include(a => a.SportSource)
                                         .Where(a => a.InsertDate >= iranStartDate)
                                         .AsNoTracking()
                                         .ToListAsync(ct);

            return allDate;

        }

        public async Task<IReadOnlyList<NewsHistory>> GetTodayNewsHistory(DateTimeOffset iranStartDate, CancellationToken ct)
        {

            // 7:00 AM Iran time today


            var allDate = await _context.NewsHistories
                .Include(a => a.News)
                .ThenInclude(n => n.SportSource)
                .Include(a => a.NewsStatus)
                .Where(a => a.InsertDate >= iranStartDate)
                .OrderByDescending(a => a.InsertDate)
                .AsNoTracking()
                .ToListAsync(ct);

            return allDate;
        }



        public async Task<News> GetLastNews(CancellationToken ct)
        {
            var item = await GetAll()
                .Where(n => n.NewsStatusId == StatusEnumeration.Sent.Id)
                .OrderByDescending(n => n.SentDate)
                .FirstOrDefaultAsync(ct);

            return item;
        }

    }
}
