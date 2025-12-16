using AngleSharp.Dom;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using TelegramAggregator.WebAPI.Data;
using TelegramAggregator.WebAPI.Data.Repositories;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models.Entities;
using TelegramAggregator.WebAPI.Models.FootbalSettings;

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

        public async Task InQueueAsync(NewsItem newsItem, FootbalForiSettings footbalForiSettings, CancellationToken ct)
        {
            var news = await GetByIdAsync(newsItem.Id.Value, ct);
            news.Score = ScoreCalculator.Calculate(newsItem, footbalForiSettings);
            news.NewsStatusId = StatusEnumeration.InQueue.Id;
            var history = news.Histories.Last();
            history.NewsStatusId = StatusEnumeration.InQueue.Id;
            _context.Update(news);

        }
        public async Task InQueueRangeAsync(List<NewsItem> newsItems, FootbalForiSettings footbalForiSettings, CancellationToken ct)
        {

            foreach (var news in newsItems)
            {
                await InQueueAsync(news, footbalForiSettings, ct);

            }
            await _context.SaveChangesAsync(ct);

        }
        public async Task DecayAsync(News news, string reason, CancellationToken ct)
        {

            news.NewsStatusId = StatusEnumeration.Decay.Id;
            var history = news.Histories.Last();
            history.NewsStatusId = StatusEnumeration.Decay.Id;
            history.Reason = reason;
            _context.Update(news);
        }
        public async Task DecayRangeAsync(List<(string Reason, NewsItem Item)> newsItems, CancellationToken ct)
        {
            var newsIds = newsItems.Select(a => a.Item.Id).ToList();
            var newsIndDb = GetAll().Where(a => newsIds.Contains(a.Id));

            foreach (var item in newsItems)
            {
                var news = await newsIndDb.FirstAsync(a => a.Id == item.Item.Id, ct);
                await DecayAsync(news, item.Reason, ct);
            }
            await _context.SaveChangesAsync();
        }


        public async Task ProcessNewsToInQueueOrDecay(List<NewsItem> newsItems, FootbalForiSettings footbalForiSettings, CancellationToken ct)
        {
            await ReceiveRangeAsync(newsItems, ct);
            var videoKeywords = footbalForiSettings.VideoKeywords;
            var otherSportsKeywords = footbalForiSettings.OtherSportsKeywords;
            var womenKeywords = footbalForiSettings.WomenKeywords;
            var cutoff = DateTimeOffset.Now.AddHours(-6);

            // استفاده از List برای نگه داشتن decay با دلیل
            List<(string Reason, NewsItem Item)> decayList = new List<(string, NewsItem)>();
            List<NewsItem> inQueueList = new List<NewsItem>();

            var eightHoursAgo = DateTime.UtcNow.AddHours(-8);
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
                else if (item.PubDate < cutoff) decayList.Add(("قدیمی‌تر از ۶ ساعت", item));

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

                if (decayList.Any())
                {
                    await DecayRangeAsync(decayList, ct);
                }

                if (inQueueList.Any())
                {
                    await InQueueRangeAsync(inQueueList, footbalForiSettings, ct);
                }
            }

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
        public async Task<long> ReceiveAsync(NewsItem newsItem, CancellationToken ct)
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

            return news.Id;
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

            // 2. Get existing links from DB
            HashSet<string> existLinks =
                GetAll()
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
            foreach (var news in newsItems)
            {
                long id = await ReceiveAsync(news, ct);
                news.Id = id;
            }
            await _context.SaveChangesAsync(ct);

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

        public Task AcceptedAsync(NewsItem newsItem, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task DecayAsync(NewsItem newsItem, string reason, CancellationToken ct)
        {
            throw new NotImplementedException();
        }


    }
}
