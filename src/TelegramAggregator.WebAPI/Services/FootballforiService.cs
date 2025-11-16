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



        public FootballforiService(
            ITelegramBotClient bot,
            ILogger<FootballforiService> logger,
            IHttpHelper httpHelper,
            IRssParser rssParser,
            NewsStorage newsStorage,
            IOptions<AppSettings> appSettings,
            IHtmlExtractor htmlExtractor,
            LineupFilter lineupFilter)
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
        }
        public async Task RunAsync(CancellationToken ct)
        {
            var publishTask = Task.CompletedTask;

            while (!ct.IsCancellationRequested)
            {
                foreach (var channel in _channels)
                {
                    try
                    {
                        _logger.LogInformation("شروع پردازش کانال: {Channel}", channel.Name);

                        // 1. شروع انتشار (فقط یک بار)
                        if (publishTask.IsCompleted)
                        {
                            publishTask = PublishContinuouslyAsync(channel, ct);
                        }

                        // 2. واکشی و اضافه به صف (همزمان)
                        await FetchAndFilterNewsAndAddToQueueAsync(channel.Sources, ct);

                        // 3. تاخیر بین کانال‌ها
                        await Task.Delay(TimeSpan.FromSeconds(10), ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "خطا در پردازش کانال {Channel}", channel.Name);
                    }
                }

                // 4. تاخیر بین چرخه‌های کامل
                
                await Task.Delay(TimeSpan.FromMinutes(3), ct);
            }

            // 5. پایان تمیز: صبر تا پایان انتشار
            _newsStorage.Complete();
            await publishTask;
        }
        private async Task FetchAndFilterNewsAndAddToQueueAsync(List<SourceConfig> sources, CancellationToken ct)
        {
            var allItems = new List<NewsItem>();
            var footballKeywords = _appSettings.FootballKeywords;
            var nonSportKeywords = _appSettings.NonSportKeywords;

            foreach (var source in sources)
            {
                try
                {
                    var rssContent = await _httpHelper.GetStringAsync(source.Url, ct);
                    allItems.AddRange(_rssParser.Parse(rssContent));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "خطا در واکشی منبع: {Source}", source.Url);
                }
            }

            var filtered = allItems
                .Where(item =>
                    footballKeywords.Any(k => (item.Title + " " + item.ShortDescription)
                        .Contains(k, StringComparison.OrdinalIgnoreCase)) &&
                    !nonSportKeywords.Any(k =>
                        item.Title.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                        item.ShortDescription.Contains(k, StringComparison.OrdinalIgnoreCase)) &&
                    string.IsNullOrEmpty(item.VideoUrl) && !_lineupFilter.IsLineupOrPreMatchNews(item)
                )
                .OrderByDescending(i => i.PubDate)
                .Take(10)
                .ToList();

            foreach (var item in filtered)
            {
                await _newsStorage.AddAsync(item, ct);
            }
        }
        private async Task PublishContinuouslyAsync(ChannelConfig channel, CancellationToken ct)
        {
            var processed = new HashSet<NewsItem>(NewsItemComparer.Instance);
            var queue = _newsStorage.GetCurrentPublishQueue();

           
            try
            {
                await foreach (var item in _newsStorage.PublishReader.ReadAllAsync(ct))
                {
                    if (processed.Contains(item)) continue;

                    if (_newsStorage.LastSourcePublished == item.SourceName &&
                        _newsStorage.HasOtherSourceSafe(item.SourceName))
                    {
                        await _newsStorage.RepublishAsync(item, ct);
                        await Task.Delay(TimeSpan.FromSeconds(2), ct);
                        continue;
                    }

                    try
                    {
                        _logger.LogInformation("Source Name ===>" + item.SourceName);
                        await SendToTelegramAsync(item, channel, ct);
                        _newsStorage.UpdateLastPublished(item.SourceName, DateTimeOffset.Now);
                        processed.Add(item);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "خطا در ارسال خبر: {Title}", item.Title);
                        await _newsStorage.RepublishAsync(item, ct);
                    }

               
                    var delay = TimeSpan.FromMinutes(Random.Shared.Next(2, 16));
                    await Task.Delay(delay, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در انتشار مداوم");
            }
        }
        private async Task SendToTelegramAsync(NewsItem item, ChannelConfig channelConfig, CancellationToken ct)
        {
            if (!ct.IsCancellationRequested)
            {

                // 1. استخراج تصویر و توضیح کامل (موازی)
                var imageTask = _htmlExtractor.ExtractMainImageAsync(item.Link, ct);
                var fullDescTask = _htmlExtractor.GetFullDescriptionAsync(item.Link, ct);

                await Task.WhenAll(imageTask, fullDescTask);

                item.ImageUrl = await imageTask;
                item.FullDescription = await fullDescTask;


                string caption = await MessageTemplateFormatter.PrepareMessageAsync(item, channelConfig, _htmlExtractor);

                Telegram.Bot.Types.Message sentMessage;

                if (!string.IsNullOrEmpty(item.VideoUrl))
                {
                    sentMessage = await _bot.SendVideo(_hiddenGroupId, item.VideoUrl,
                        caption: caption, parseMode: ParseMode.Html, disableNotification: true);
                }
                else if (!string.IsNullOrEmpty(item.ImageUrl))
                {
                    sentMessage = await _bot.SendPhoto(_hiddenGroupId, item.ImageUrl,
                        caption: caption, parseMode: ParseMode.Html, disableNotification: true);
                }
                else
                {
                    sentMessage = await _bot.SendMessage(_hiddenGroupId, caption,
                        parseMode: ParseMode.Html, disableNotification: true);
                }

                await _bot.CopyMessage(channelConfig.ChannelId,
                    _hiddenGroupId, sentMessage.MessageId,
                    disableNotification: true);

                await _bot.DeleteMessage(_hiddenGroupId, sentMessage.MessageId);

            }

        }

        
    }
}
