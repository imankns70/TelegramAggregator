using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramAggregator.Infrastructure;
using TelegramAggregator.Services;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Services
{
    public partial class FootballforiService
    {
        private readonly ITelegramBotClient _bot;
        private readonly ILogger<FootballforiService> _logger;
        private readonly ChannelConfig footbalForiChannel;
        private readonly IHttpHelper _httpHelper;
        private readonly IRssParser _rssParser;
        private readonly NewsStorage _newsStorage;
        private readonly IImageExtractor _imageExtractor;
        private readonly IFullDecriptionExtractor _fullDecriptionExtractor;
        private readonly AppSettings _appSettings;
        private readonly long _hiddenGroupId;
        private readonly Random _rnd = Random.Shared;

        public FootballforiService(
            ITelegramBotClient bot,
            ILogger<FootballforiService> logger,
            IHttpHelper httpHelper,
            IRssParser rssParser,
            NewsStorage newsStorage,
            IOptions<AppSettings> appSettings,
            IImageExtractor imageExtractor,
            IFullDecriptionExtractor fullDecriptionExtractor)
        {
            _bot = bot;
            _logger = logger;
            _appSettings = appSettings.Value;
            _httpHelper = httpHelper;
            _rssParser = rssParser;
            _newsStorage = newsStorage;
            _hiddenGroupId = _appSettings.HiddenGroupId;
            _imageExtractor = imageExtractor;
            footbalForiChannel = _appSettings.Channels[0];
            _fullDecriptionExtractor = fullDecriptionExtractor;
        }

        // این متد فقط یک بار توسط BackgroundService کال میشه
        public async Task StartAsync(CancellationToken ct)
        {
            _logger.LogInformation("FootballforiService استارت شد — پادشاه بیدار شد");

            // فقط دو تسک همزمان و مستقل
            var publishTask = Task.Run(() => PublishContinuouslyAsync(ct), ct);
            var fetchTask = Task.Run(() => StartFetchingLoopAsync(ct), ct);

            // منتظر می‌مونیم تا هر دو تموم بشن (وقتی سرویس خاموش شد)
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
                    await FetchAndFilterNewsAndAddToQueueAsync(footbalForiChannel.Sources, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "خطا در FetchAndFilterNewsAndAddToQueueAsync");
                }

                // حالت عادی: هر ۳ دقیقه
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
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

            if (!allItems.Any())
            {
                _logger.LogInformation("هیچ خبری از هیچ منبعی دریافت نشد.");
                return;
            }


            var videoKeywords = _appSettings.VideoKeywords;


            var acceptedItems = new List<NewsItem>();


            //var darad = await _htmlExtractor.HasVideoAsync("https://www.tarafdari.com/node/2687956");
            //var nadarad = await _htmlExtractor.HasVideoAsync("https://www.tarafdari.com/node/2687946");

            // مرحله ۲: فیلتر اولیه + ثبت در دیباگ (فقط رد شده‌ها و قبول اولیه)
            foreach (var item in allItems)
            {
                var text = $"{item.Title} {item.ShortDescription}".ToLowerInvariant();

                if (videoKeywords.Any(k => text.Contains(k.ToLowerInvariant())))
                {
                    _newsStorage.LogRejected(item, "حاوی ویدئو/پخش زنده (رد شده)");
                    continue;

                }
                else if (item.PubDate < cutoff)
                {
                    _newsStorage.LogRejected(item, "قدیمی‌تر از ۶ ساعت");
                    continue;
                }

                else if (item.Title.Length < 15)
                {
                    _newsStorage.LogRejected(item, "عنوان خیلی کوتاه");
                    continue;
                }


                // خبر قبول شد !
                _newsStorage.LogAccepted(item);
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
                await _newsStorage.AddAsync(item, ct);

                _logger.LogInformation("نهایی و در صف → [{Source}] {Title}", item.SourceName, item.Title.Truncate(80));

            }


        }
        private async Task PublishContinuouslyAsync(CancellationToken ct)
        {
            _logger.LogInformation("PublishContinuouslyAsync شروع شد — تا ابد زنده می‌مونه");

            while (!ct.IsCancellationRequested)
            {
                var availableChannels = _newsStorage._sourceChannels
                        .Values
                        .Where(ch => ch.Reader.Count > 0)
                        .ToList();
                if (!_newsStorage.TryDequeueAny(out var item))
                {
                    await Task.Delay(3000, ct);
                    continue;
                }

                try
                {
                    // وقتی آماده شدی اینو فعال کن:
                    await SendToTelegramAsync(item, footbalForiChannel, ct);

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
        private async Task SendToTelegramAsync(NewsItem item, ChannelConfig channelConfig, CancellationToken ct)
        {
            var fullDescTask = _fullDecriptionExtractor.GetFullDescriptionAsync(item.Link);
            var imageTask = _imageExtractor.ExtractMainImageAsync(item.Link, ct);
            var hasVideoTask = _imageExtractor.HasVideoAsync(item.Link, ct);
            await Task.WhenAll(imageTask, fullDescTask, hasVideoTask);

            item.ImageUrl = await imageTask;
            item.FullDescription = await fullDescTask;
            bool hasVideo = await hasVideoTask;
            if (hasVideo)
            {
                _newsStorage.LogRejected(item, "حاوی تگ ویدئو");
                await Task.Delay(3000, ct);

            }

            else
            {

                _newsStorage.LogPublished(item);

                var (caption, keyboard) = MessageTemplateFormatter.GetPreviewMessageWithInlineButton(item, _appSettings.BotUserName);


                Message sentMessage;
                if (!string.IsNullOrEmpty(item.ImageUrl))
                {
                    sentMessage = await _bot.SendPhoto(
                        chatId: channelConfig.ChannelId, // 👈 مستقیماً کانال
                        photo: new InputFileUrl(item.ImageUrl),
                        caption: caption,
                        parseMode: ParseMode.Html,
                        replyMarkup: keyboard,
                        cancellationToken: ct);
                }
                else
                {
                    sentMessage = await _bot.SendMessage(
                        chatId: channelConfig.ChannelId, // 👈 مستقیماً کانال
                        text: caption,
                        parseMode: ParseMode.Html,
                        replyMarkup: keyboard,
                        cancellationToken: ct);
                }

                int delayMs = _rnd.Next(120_000, 900_000);

                await Task.Delay(TimeSpan.FromMinutes(1), ct);

            }

        }
    }
}