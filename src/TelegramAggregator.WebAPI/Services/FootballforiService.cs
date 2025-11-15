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
            while (!ct.IsCancellationRequested)
            {
                foreach (var channel in _channels)
                {
                    try
                    {
                        _logger.LogInformation("🚀 شروع پردازش کانال: {Channel}", channel.Name);

                        await FetchAndFilterNewsAsync(channel.Sources, ct);
                        await PublishAsync(channel, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "⚠️ خطا در پردازش کانال {Channel}", channel.Name);
                    }
                }
            }

        }
        /// <summary>
        /// واکشی + فیلتر + اضافه به NewsStorage
        /// </summary>
        private async Task FetchAndFilterNewsAsync(List<SourceConfig> sources, CancellationToken ct)
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

            // فیلتر: فقط اخبار فوتبال، بدون ویدیو، بدون غیرورزشی
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
                .Take(10) // فقط 10 خبر برتر
                .ToList();

            // اضافه به NewsStorage (خودش تکرار، تاریخ و ظرفیت رو چک می‌کنه)
            foreach (var item in filtered)
            {
                await _newsStorage.AddAsync(item, ct);
            }
        }
        private async Task PublishAsync(ChannelConfig channel, CancellationToken ct)
        {
            await foreach (var item in _newsStorage.PublishReader.ReadAllAsync(ct))
            {
                // فقط چک — بدون خالی کردن صف
                if (_newsStorage.LastSourcePublished == item.SourceName &&
                    _newsStorage.HasOtherSource(item.SourceName))
                {
                    // عقب ببر
                    await _newsStorage.RepublishAsync(item, ct);
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    continue;
                }

                _logger.LogInformation("Source Name ===>" + item.SourceName);
                //await SendToTelegramAsync(item, channel, ct);

                _newsStorage.UpdateLastPublished(item.SourceName, DateTimeOffset.Now);

                var delay = TimeSpan.FromSeconds(3);
                //var delay = TimeSpan.FromMinutes(Random.Shared.Next(2, 16));
                await Task.Delay(delay, ct);
            }
        }

        private async Task SendToTelegramAsync(NewsItem item, ChannelConfig channelConfig, CancellationToken ct)
        {
            if (!ct.IsCancellationRequested)
            {

                item.ImageUrl = await _htmlExtractor.ExtractMainImageAsync(item.Link);
                item.FullDescription = await _htmlExtractor.GetFullDescriptionAsync(item.Link);



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
