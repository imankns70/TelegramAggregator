using AngleSharp.Dom;
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
            .Include(a => a.Histories).FirstOrDefaultAsync(ct);
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
            _logger.LogDebug($"این خبر با آی دی {newsItem.Link}  برای تغییر وضعیت آن از دریافت به صف یاتف نشد");


        }
        public async Task InQueueRangeAsync(List<NewsItem> newsItems, FootbalForiSettings footbalForiSettings, CancellationToken ct)
        {

            foreach (var news in newsItems)
            {
                await InQueueAsync(news, footbalForiSettings, ct);

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
            var Ids = newsItems.Select(a => a.Item.Id).ToHashSet();
            var newsInDb = await GetAll().Where(a => Ids.Contains(a.Id)).ToListAsync(ct);


            foreach (var item in newsItems)
            {
                var news = newsInDb.First(a => a.Id == item.Item.Id);
                await DecayAsync(news, item.Reason, ct);


            }
            await _context.SaveChangesAsync();
        }


        public async Task ProcessNewsAsync(List<NewsItem> newsItems, FootbalForiSettings footbalForiSettings, CancellationToken ct)
        {

            await ReceiveRangeAsync(newsItems, ct);
            var videoKeywords = footbalForiSettings.VideoKeywords;
            var otherSportsKeywords = footbalForiSettings.OtherSportsKeywords;
            var womenKeywords = footbalForiSettings.WomenKeywords;
            //var cutoff = DateTimeOffset.Now.AddHours(-6);

            // استفاده از List برای نگه داشتن decay با دلیل
            List<(string Reason, NewsItem Item)> decayList = new List<(string, NewsItem)>();
            List<NewsItem> inQueueList = new List<NewsItem>();

            var eightHoursAgo = DateTimeOffset.UtcNow.AddHours(-8);
            var dbCandidates = GetAll()
                .Where(n => n.InsertDate >= eightHoursAgo)
                .ToList();

            foreach (var item in newsItems)
            {
                var text = $"{item.Title} {item.ShortDescription}".ToLowerInvariant();
                bool isDecay = false;

                int sportSourceId = GetSportSourceId(item.SourceName);


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
                .Where(n => n.NewsStatusId == StatusEnumeration.InQueue.Id)
                .OrderByDescending(n => n.Score)
                .ThenBy(n => n.PubDate)
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

            int sportSourceId = GetSportSourceId(news.First().SourceName);

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

            foreach (var item in newsItems)
            {
                var now = DateTimeOffset.UtcNow;
                string cleanLink = ClearUrl(item.Link);
                int sportSourceId = GetSportSourceId(item.SourceName);
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

                await _context.AddAsync(news);
                await _context.SaveChangesAsync(ct);
                item.Id = news.Id;

            }


        }


        public async Task ChangeNewsInQueueToExpiredState(CancellationToken ct)
        {
            var yest = DateTimeOffset.UtcNow.AddDays(-1);
            var inQueueNews = await GetAll()
                                         .Include(n => n.Histories)
                                         .Where(n => (n.NewsStatusId == StatusEnumeration.InQueue.Id || n.NewsStatusId == StatusEnumeration.Receive.Id)
                                         && n.InsertDate.Date >= yest.Date)
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
            var uri = new Uri(url);

            var clean = uri.GetLeftPart(UriPartial.Path);

            if (clean.EndsWith("/"))
                clean = clean.TrimEnd('/');

            return clean.ToLowerInvariant();
        }

        private int GetSportSourceId(string sourceName)
        {
            if (sourceName == "ورزش سه | آخرین اخبار ورزشی")
                return SportSourceEnumeration.Varzesh3.Id;
            else if (sourceName == "طرفداری")
                return SportSourceEnumeration.Tarafdari.Id;
            else if (sourceName == "خبرورزشی؛ آخرین اخبار ورزشی ایران و جهان")
                return SportSourceEnumeration.KhabarVarzeshi.Id;
            return SportSourceEnumeration.Football360.Id;
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
