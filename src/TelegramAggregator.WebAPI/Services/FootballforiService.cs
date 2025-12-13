using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Runtime;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Infrastructure.Storage;
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Models.FootbalSettings;

namespace TelegramAggregator.WebAPI.Services
{
    public partial class FootballforiService
    {
        private readonly ITelegramBotClient _bot;
        private readonly ILogger<FootballforiService> _logger;
        private readonly IHttpHelperService _httpHelper;
        private readonly IRssParserService _rssParser;
        private readonly INewsStorage _newsStorage;
        private readonly IDebugStore _debugStore;
        private readonly IImageExtractorService _imageExtractor;
        private readonly IFullDecriptionExtractorService _fullDecriptionExtractor;
        private readonly FootbalForiSettings _footbalForiSettings;
        private readonly IFootball360ScraperService _football360ScraperService;
        private readonly Random _rnd = Random.Shared;

        public FootballforiService(
            ITelegramBotClient bot,
            ILogger<FootballforiService> logger,
            IHttpHelperService httpHelper,
            IRssParserService rssParser,
            INewsStorage newsStorage,
            IOptions<AppSettings> appSettings,
            IImageExtractorService imageExtractor,
            IFullDecriptionExtractorService fullDecriptionExtractor,
            IHostEnvironment environment,
            IDebugStore debugStore,
            IFootball360ScraperService football360ScraperService)
        {
            _bot = bot;
            _logger = logger;
            _footbalForiSettings = appSettings.Value.FootbalForiSettings;
            _httpHelper = httpHelper;
            _rssParser = rssParser;
            _newsStorage = newsStorage;
            _imageExtractor = imageExtractor;
            _fullDecriptionExtractor = fullDecriptionExtractor;
            _debugStore = debugStore;
            _football360ScraperService = football360ScraperService;
        }


        public async Task StartAsync(CancellationToken ct)
        {
            _logger.LogInformation("FootballforiService استارت شد — پادشاه بیدار شد");


            var publishTask = Task.Run(() => PublishContinuouslyAsync(ct), ct);
            var fetchTask = Task.Run(() => StartFetchingLoopAsync(ct), ct);

            await Task.WhenAll(publishTask, fetchTask);
        }

        private async Task StartFetchingLoopAsync(CancellationToken ct)
        {
            _logger.LogInformation("شروع حلقه واکشی RSS — هر ۳ دقیقه یکبار (شب‌ها ۳۰ دقیقه)");

            while (!ct.IsCancellationRequested)
            {
                var now = DateTimeOffset.Now;

                // حالت شبانه: ۱ تا ۸ صبح → هر ۳۰ دقیقه
                if (now.Hour >= 1 && now.Hour < 8)
                {
                    _logger.LogInformation("حالت شبانه فعال — تاخیر ۳۰ دقیقه");
                    await Task.Delay(TimeSpan.FromMinutes(30), ct);
                    continue;
                }

                try
                {
                    await FetchAndFilterNewsAndAddToQueueAsync(_footbalForiSettings.Sources, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "خطا در FetchAndFilterNewsAndAddToQueueAsync");
                }

                // حالت عادی: هر 5 دقیقه
                await Task.Delay(TimeSpan.FromMinutes(10), ct);
            }

            _logger.LogWarning("حلقه واکشی RSS خاتمه یافت");
        }
        private async Task FetchAndFilterNewsAndAddToQueueAsync(List<SourceConfig> sources, CancellationToken ct)
        {
            var allItems = new List<NewsItem>();
            var cutoff = DateTimeOffset.Now.AddHours(-6);

            // مرحله ۱: واکشی همه RSSها
            foreach (var source in sources)
            {
                try
                {
                    var rssContent = await _httpHelper.GetStringAsync(source.Url, ct);
                    if (string.IsNullOrWhiteSpace(rssContent))
                    {
                        _logger.LogWarning("RSS خالی از {Source}", source.Name);
                        continue;
                    }

                    var parsed = _rssParser.Parse(rssContent);
                    allItems.AddRange(parsed);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "خطا در واکشی منبع: {Source} | {Url}", source.Name, source.Url);
                }
            }



            var football360News = await _football360ScraperService.GetLatestNewsAsync(ct);
            allItems.AddRange(football360News);

            var videoKeywords = _footbalForiSettings.VideoKeywords;
            var otherSportsKeywords = _footbalForiSettings.OtherSportsKeywords;
            var womenKeywords = _footbalForiSettings.WomenKeywords;

            var acceptedItems = new List<NewsItem>();


            // مرحله ۲: فیلتر اولیه + ثبت در دیباگ (فقط رد شده‌ها و قبول اولیه)
            foreach (var item in allItems)
            {
                var text = $"{item.Title} {item.ShortDescription}".ToLowerInvariant();

                var otherSport = otherSportsKeywords.FirstOrDefault(k => text.Contains(k.ToLowerInvariant()));
                if (!string.IsNullOrWhiteSpace(otherSport))
                {
                    _debugStore.LogRejected(item, otherSport);
                    continue;

                }
                else if (videoKeywords.Any(k => text.Contains(k.ToLowerInvariant())))
                {
                    _debugStore.LogRejected(item, "عنوان ویدئو");
                    continue;

                }
                else if (womenKeywords.Any(k => text.Contains(k.ToLowerInvariant())))
                {
                    _debugStore.LogRejected(item, "فوتبال بانوان");
                    continue;

                }
                else if (text.Contains("ترکیب رسمی"))
                {
                    _debugStore.LogRejected(item, "ترکیب رسمی");
                    continue;

                }
                else if (text.Contains("جدول پخش زنده"))
                {
                    _debugStore.LogRejected(item, "جدول پخش زنده");
                    continue;

                }
                else if (text.Contains("روزنامه خبرورزشی"))
                {
                    _debugStore.LogRejected(item, "روزنامه خبرورزشی");
                    continue;

                }
                else if (item.PubDate < cutoff)
                {
                    _debugStore.LogRejected(item, "قدیمی‌تر از ۶ ساعت");
                    continue;
                }

                item.Score = ScoreCalculator.Calculate(item, _footbalForiSettings);

                // خبر قبول شد !
                _debugStore.LogAccepted(item);
                _logger.LogInformation("قبول شد ← [{Source}] {Title}", item.SourceName, item.Title.Truncate(80));
                acceptedItems.Add(item);
            }

            if (!acceptedItems.Any())
            {
                _logger.LogInformation("بعد از فیلتر هیچ خبری برای بالانس باقی نماند.");
                return;
            }


            foreach (var item in acceptedItems)
            {
                (bool, string) result = await _newsStorage.TryAddAsync(item, ct);
                if (result.Item1)
                    _debugStore.LogAccepted(item);
                else
                    _debugStore.LogRejected(item, result.Item2);


                _logger.LogInformation("نهایی و در صف → [{Source}] {Title}", item.SourceName, item.Title.Truncate(80));

            }


        }
        private async Task PublishContinuouslyAsync(CancellationToken ct)
        {
            _logger.LogInformation("PublishContinuouslyAsync شروع شد — تا ابد زنده می‌مونه");

            while (!ct.IsCancellationRequested)
            {

                if (!_newsStorage.TryDequeueAny(out var item))
                {
                    await Task.Delay(3000, ct);
                    continue;
                }

                try
                {
                    // وقتی آماده شدی اینو فعال کن:
                    await SendToTelegramAsync(item, _footbalForiSettings.ChannelId, ct);

                    // تست: فرض می‌کنیم ارسال موفق بود
                    _logger.LogInformation("ارسال شد → [{Source}] {Title}", item.SourceName, item.Title.Truncate(60));



                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "خطا در ارسال خبر به تلگرام: {Title}", item?.Title);
                    await Task.Delay(5000, ct);
                }

                _logger.LogWarning("PublishContinuouslyAsync خاتمه یافت");

            }
        }
        private async Task SendToTelegramAsync(NewsItem item, long channelId, CancellationToken ct)
        {
            bool hasVideo = false;

            // همیشه این دو Task مشترک هستند
            var imageTask = _imageExtractor.ExtractMainImageAsync(item.Link, ct);
            var hasVideoTask = _imageExtractor.HasVideoAsync(item.Link, ct);

            Task<string> fullDescTask = null;

            if (!item.SourceName.Contains("360"))
            {
                // فقط برای منابع غیر 360
                fullDescTask = _fullDecriptionExtractor.GetFullDescriptionAsync(item.Link);
                await Task.WhenAll(imageTask, hasVideoTask, fullDescTask);
            }
            else
            {
                await Task.WhenAll(imageTask, hasVideoTask);
            }

            item.ImageUrl = await imageTask;
            hasVideo = await hasVideoTask;

            if (fullDescTask != null)
                item.FullDescription = await fullDescTask;

            if (string.IsNullOrEmpty(item.ImageUrl))
            {
                _debugStore.LogRejected(item, "خبر بدون عکس");
                await Task.Delay(3000, ct);
            }

            else if (hasVideo)
            {
                _debugStore.LogRejected(item, "حاوی تگ ویدئو");
                await Task.Delay(3000, ct);

            }
            else if (item.FullDescription.Contains("خطا در دریافت محتوا"))
            {
                _debugStore.LogRejected(item, "خطا در واکشی شرح کامل");
                await Task.Delay(3000, ct);
            }
            else
            {

                _debugStore.LogPublished(item);

                var caption = CaptionMakerExtension.GetCaption(item);

                Message sentMessage;

                if (!string.IsNullOrEmpty(item.ImageUrl))
                {
                    sentMessage = await _bot.SendPhoto(
                        chatId: channelId, // 👈 مستقیماً کانال
                        photo: new InputFileUrl(item.ImageUrl),
                        caption: caption,
                        parseMode: ParseMode.Html,
                        cancellationToken: ct);
                }
                else
                {
                    sentMessage = await _bot.SendMessage(
                        chatId: channelId, // 👈 مستقیماً کانال
                        text: caption,
                        parseMode: ParseMode.Html,
                        cancellationToken: ct);
                }


                // await Task.Delay(TimeSpan.FromSeconds(15), ct);
                await Task.Delay(TimeSpan.FromMinutes(_rnd.Next(4, 35)), ct);

            }

        }
    }
}