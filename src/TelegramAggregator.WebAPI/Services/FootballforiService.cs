using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TelegramAggregator.Infrastructure;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Services
{
    public partial class FootballforiService
    {
        private readonly ITelegramBotClient _bot;
        private readonly ILogger<FootballforiService> _logger;
        private readonly List<ChannelConfig> _channels;
        private readonly IHttpHelper _httpHelper;
        private readonly IRssParser _rssParser;
        private readonly NewsStorage _newsStorage;
        private readonly IHtmlExtractor _htmlExtractor;
        private readonly AppSettings _appSettings;
        private readonly long _hiddenGroupId;
        private readonly LineupFilter _lineupFilter;
        private readonly DebugStorage _debugStorage;
        private readonly Random _random = new();

        public FootballforiService(
            ITelegramBotClient bot,
            ILogger<FootballforiService> logger,
            IHttpHelper httpHelper,
            IRssParser rssParser,
            NewsStorage newsStorage,
            IOptions<AppSettings> appSettings,
            IHtmlExtractor htmlExtractor,
            LineupFilter lineupFilter,
            DebugStorage debugStorage)
        {
            _bot = bot;
            _logger = logger;
            _appSettings = appSettings.Value;
            _httpHelper = httpHelper;
            _rssParser = rssParser;
            _newsStorage = newsStorage;
            _hiddenGroupId = _appSettings.HiddenGroupId;
            _htmlExtractor = htmlExtractor;
            _channels = _appSettings.Channels;
            _lineupFilter = lineupFilter;
            _debugStorage = debugStorage;
        }

        // این متد فقط یک بار توسط BackgroundService کال میشه
        public async Task StartAsync(CancellationToken ct)
        {
            _logger.LogInformation("FootballforiService استارت شد");

            // فقط یک بار! Publish تا ابد زنده می‌مونه
            var publishTask = PublishContinuouslyAsync(ct);

            // Fetch هر 3 دقیقه (با حالت شبانه)
            var fetchTask = StartFetchingLoopAsync(ct);

            await Task.WhenAll(publishTask, fetchTask);
        }

        private async Task StartFetchingLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var now = DateTime.Now;
                if (now.Hour >= 1 && now.Hour < 8)
                {
                    _logger.LogInformation("حالت شبانه فعال — تاخیر 30 دقیقه");
                    await Task.Delay(TimeSpan.FromMinutes(30), ct);
                    continue;
                }

                try
                {
                    foreach (var channel in _channels)
                    {
                        await FetchAndFilterNewsAndAddToQueueAsync(channel.Sources, ct);
                        await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "خطا در چرخه Fetch");
                }

                await Task.Delay(TimeSpan.FromMinutes(3), ct);
            }
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

            var footballKeywords = _appSettings.FootballKeywords;
            var trashRegexes = _appSettings.FilterSettings.TrashPatterns
                .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .ToList();

            var acceptedItems = new List<NewsItem>();

            // مرحله ۲: فیلتر دقیق + استخراج محتوا + چک ویدیو
            foreach (var item in allItems)
            {
                var text = $"{item.Title} {item.ShortDescription}".ToLowerInvariant();
                string? rejectReason = null;

                // ۱. تاریخ
                if (item.PubDate < cutoff)
                    rejectReason = "قدیمی‌تر از ۶ ساعت";

                // ۲. الگوهای آشغال (گل لحظه‌ای، پیش‌بینی، هشتگ و ...)
                else if (trashRegexes.Any(r => r.IsMatch(text)))
                    rejectReason = "گل لحظه‌ای / تیم ملی / پیش‌بینی / هشتگ";

                // ۳. عنوان خیلی کوتاه
                else if (item.Title.Length < 20)
                    rejectReason = "عنوان خیلی کوتاه";

                // ۴. ترکیب احتمالی یا پیش‌بازی
                else if (_lineupFilter.IsLineupOrPreMatchNews(item))
                    rejectReason = "ترکیب احتمالی یا پیش‌بازی";

                // ۵. بدون کلمه کلیدی مهم
                else if (!footballKeywords.Any(k => text.Contains(k.ToLowerInvariant())))
                    rejectReason = "کلمه کلیدی فوتبال ندارد";

                // ۶. چک ویدیو (مهم‌ترین درخواست تو!)
                else
                {
                    var hasVideo = await _htmlExtractor.HasVideoAsync(item.Link, ct);
                    if (hasVideo)
                        rejectReason = "حاوی ویدیو (حذف کامل)";
                }

                // ثبت در دیباگ (همه خبرها — قبول یا رد)
                _debugStorage.Add(new DebugFeedItem
                {
                    SourceName = item.SourceName!,
                    Title = item.Title,
                    Link = item.Link,
                    PubDate = item.PubDate,
                    IsAccepted = rejectReason == null,
                    RejectReason = rejectReason,
                    ProcessedAt = DateTimeOffset.Now
                });

                if (rejectReason != null)
                {
                    _logger.LogWarning("رد شد ← [{Source}] {Title} | دلیل: {Reason}",
                        item.SourceName, item.Title.Truncate(80), rejectReason);
                    continue;
                }

                // خبر قبول شد → استخراج عکس و متن کامل (برای ارسال بهتر و دیباگ دقیق‌تر)
                try
                {
                    var imageTask = _htmlExtractor.ExtractMainImageAsync(item.Link, ct);
                    var descTask = _htmlExtractor.GetFullDescriptionAsync(item.Link, ct);

                    await Task.WhenAll(imageTask, descTask);

                    item.ImageUrl = await imageTask;
                    item.FullDescription = await descTask;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "خطا در استخراج محتوا برای خبر: {Title}", item.Title.Truncate(80));
                    item.ImageUrl = null;
                    item.FullDescription = "متن کامل در دسترس نیست.";
                }

                _logger.LogInformation("قبول شد و آماده ارسال ← [{Source}] {Title}", item.SourceName, item.Title.Truncate(100));
                acceptedItems.Add(item);
            }

            // مرحله ۳: فقط ۱۰ خبر جدیدتر و طلایی رو به صف بفرست
            var finalItems = acceptedItems
                .OrderByDescending(i => i.PubDate)
                .Take(10)
                .ToList();

            foreach (var item in finalItems)
            {
                await _newsStorage.AddAsync(item, ct);
            }

            _logger.LogInformation(
                "چرخه تمام شد → کل: {Total} | قبول: {Accepted} | رد: {Rejected} | به صف: {Queued}",
                allItems.Count,
                acceptedItems.Count,
                allItems.Count - acceptedItems.Count,
                finalItems.Count
            );
        }

        private async Task PublishContinuouslyAsync(CancellationToken ct)
        {
            _logger.LogInformation("PublishContinuouslyAsync شروع شد — تا ابد زنده می‌مونه");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var item = await _newsStorage.PublishReader.ReadAsync(ct);

                    if (item?.Link == null) continue;

                    var itemKey = $"{item.SourceName}:{item.Link}";
                    if (_newsStorage.IsAlreadyProcessed(itemKey)) continue;

                    if (_newsStorage.LastSourcePublished == item.SourceName &&
                        _newsStorage.HasOtherSourceSafe(item.SourceName))
                    {
                        await _newsStorage.RepublishAsync(item, ct);
                        await Task.Delay(TimeSpan.FromSeconds(2), ct);
                        continue;
                    }

                    foreach (var channel in _channels)
                    {
                        try
                        {
                            _logger.LogInformation($"SourceName::::{item.SourceName}");
                           // await SendToTelegramAsync(item, channel, ct);
                            _newsStorage.UpdateLastPublished(item.SourceName!, DateTimeOffset.Now);
                            _newsStorage.MarkAsProcessed(itemKey);

                            //var delay = TimeSpan.FromMinutes(_random.Next(2, 17));
                            var delay = TimeSpan.FromSeconds(3);
                            await Task.Delay(delay, ct);
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "خطا در ارسال به کانال {Channel}", channel.Name);
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "خطای غیرمنتظره در Publish");
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                }
            }

            _logger.LogWarning("PublishContinuouslyAsync خاتمه یافت");
        }

        private async Task SendToTelegramAsync(NewsItem item, ChannelConfig channelConfig, CancellationToken ct)
        {
            var imageTask = _htmlExtractor.ExtractMainImageAsync(item.Link, ct);
            var fullDescTask = _htmlExtractor.GetFullDescriptionAsync(item.Link, ct);
            await Task.WhenAll(imageTask, fullDescTask);

            item.ImageUrl = await imageTask;
            item.FullDescription = await fullDescTask;

            var caption = await MessageTemplateFormatter.PrepareMessageAsync(item, channelConfig, _htmlExtractor);

            Telegram.Bot.Types.Message sentMessage;

            if (!string.IsNullOrEmpty(item.VideoUrl))
                sentMessage = await _bot.SendVideo(_hiddenGroupId, item.VideoUrl, caption: caption, parseMode: ParseMode.Html);
            else if (!string.IsNullOrEmpty(item.ImageUrl))
                sentMessage = await _bot.SendPhoto(_hiddenGroupId, item.ImageUrl, caption: caption, parseMode: ParseMode.Html);
            else
                sentMessage = await _bot.SendMessage(_hiddenGroupId, caption, parseMode: ParseMode.Html);

            await _bot.CopyMessage(channelConfig.ChannelId, _hiddenGroupId, sentMessage.MessageId);
            await _bot.DeleteMessage(_hiddenGroupId, sentMessage.MessageId);
        }
    }
}