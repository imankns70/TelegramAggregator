using Microsoft.EntityFrameworkCore;
using TelegramAggregator.WebAPI.Data;
using TelegramAggregator.WebAPI.Data.Repositories;
using TelegramAggregator.WebAPI.Models.Entities;

namespace TelegramAggregator.WebAPI.Models.Repositories
{
    public class NewsRepository : INewsRepository
    {
        private readonly TelegramAggregatorContext _context;

        public NewsRepository(TelegramAggregatorContext context)
        {
            _context = context;
        }

        public IQueryable<News> GetAll()
        {
            return _context.News.AsQueryable();
        }

        public async Task<News> GetByIdAsync(long id, CancellationToken ct)
        {
            return await _context.News
            .Include(a => a.Histories).FirstOrDefaultAsync(ct);
        }

        public async Task AcceptedAsync(NewsItem newsItem, CancellationToken ct)
        {
            var news = await GetByIdAsync(newsItem.Id.Value, ct);
            news.NewsStatusId = StatusEnumeration.Accepted.Id;
            var history = news.Histories.Last();
            history.NewsStatusId = StatusEnumeration.Accepted.Id;
            _context.Update(news);
            await _context.SaveChangesAsync(ct);
        }
        public async Task DecayAsync(NewsItem newsItem, string reason, CancellationToken ct)
        {
            var news = await GetByIdAsync(newsItem.Id.Value, ct);
            news.NewsStatusId = StatusEnumeration.Decay.Id;
            var history = news.Histories.Last();
            history.NewsStatusId = StatusEnumeration.Decay.Id;
            history.Reason = reason;
            _context.Update(news);
            await _context.SaveChangesAsync(ct);
        }
        public async Task PublishedAsync(NewsItem newsItem, CancellationToken ct)
        {
            var news = await GetByIdAsync(newsItem.Id.Value, ct);
            news.ImageUrl = newsItem.ImageUrl;
            news.FullDescription = newsItem.FullDescription;
            news.NewsStatusId = StatusEnumeration.Sent.Id;
            var history = news.Histories.Last();
            history.NewsStatusId = StatusEnumeration.Sent.Id;           
            _context.Update(news);
            await _context.SaveChangesAsync(ct);
        }
        public async Task ReceiveAsync(NewsItem newsItem, CancellationToken ct)
        {
            var now = DateTime.Now;
            string globalKey = GenerateGlobalKey(newsItem.Link);
            int sportSourceId = GetSportSourceId(newsItem.SourceName);
            var news = new News
            {
                Link = newsItem.Link,
                InsertDate = now,
                Title = newsItem.Title,
                ShortDescription = newsItem.ShortDescription,
                PubDate = newsItem.PubDate.DateTime,
                GlobalKey = globalKey,
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

        }
        public async Task UpdateScoresync(NewsItem newsItem, CancellationToken ct)
        {
            var news = await GetByIdAsync(newsItem.Id.Value, ct);
            news.Score = newsItem.Score;
            _context.Update(news);
            await _context.SaveChangesAsync(ct);
        }
        private string GenerateGlobalKey(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
                return Guid.NewGuid().ToString(); // fallback

            // فقط قسمت اصلی لینک رو نگه دار (بدون پارامتر و hash)
            var uri = new Uri(link);
            var cleanPath = uri.AbsolutePath;

            // حذف اسلش آخر اگر بود
            if (cleanPath.EndsWith("/"))
                cleanPath = cleanPath.TrimEnd('/');

            // ترکیب هاست + مسیر تمیز = کلید ۱۰۰٪ یکتا
            return $"{uri.Host}{cleanPath}".ToLowerInvariant();
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
    }
}
