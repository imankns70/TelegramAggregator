using AngleSharp.Dom;
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
            string cleanLink = ClearUrl(newsItem.Link);
            int sportSourceId = GetSportSourceId(newsItem.SourceName);
            var news = new News
            {
                Link = cleanLink,
                InsertDate = now,
                Title = newsItem.Title,
                ShortDescription = newsItem.ShortDescription,
                PubDate = newsItem.PubDate.DateTime,
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
        public void FilterNews(ref List<NewsItem> news)
        {
            if (news.Any())
            {
                int sportSourceId = GetSportSourceId(news.First().SourceName);
                HashSet<string> links = new HashSet<string>(news.Select(a => ClearUrl(a.Link)));
                HashSet<string> existLinks = new HashSet<string>(
                    GetAll().Where(a => a.SportSourceId == sportSourceId &&
                                        a.InsertDate > DateTime.Now.AddDays(-3) &&
                                        links.Contains(a.Link))
                            .Select(a => a.Link)
                );

                // 4. فیلتر کردن اخبار و حذف لینک‌های تکراری
                news = news.Where(n => !existLinks.Contains(n.Link)).ToList();
            }

        }

        private string ClearUrl(string url)
        {
            var uri = new Uri(url);

            // فقط قسمت قبل از ? (پارامترها حذف)
            var path = uri.GetLeftPart(UriPartial.Path);

            // حذف اسلش آخر
            if (path.EndsWith("/"))
                path = path.TrimEnd('/');

            // ترکیب هاست + مسیر تمیز
            return $"{uri.Host}{path}".ToLowerInvariant();
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
