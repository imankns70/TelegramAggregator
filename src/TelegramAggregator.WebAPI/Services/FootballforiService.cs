using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TelegramAggregator.Infrastructure;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Services
{
    public class FootballforiService
    {
        private readonly ITelegramBotClient _bot;
        private readonly ILogger<FootballforiService> _logger;
        private readonly List<ChannelConfig> _channels;
        private readonly HttpHelper _httpHelper;
        private readonly RssParser _rssParser;
        private readonly DuplicateChecker _duplicateChecker;
        private readonly NewsStorage _newsStorage;
        private readonly HtmlExtractor _htmlExtractor;
        private readonly AppSettings _appSettings;
        private readonly long _hiddenGroupId;
        private readonly Dictionary<string, DateTime> _lastPublishedBySource = new();
        private readonly Random _random = new();

        public FootballforiService(
            ITelegramBotClient bot,
            ILogger<FootballforiService> logger,
            HttpHelper httpHelper,
            RssParser rssParser,
            DuplicateChecker duplicateChecker,
            NewsStorage newsStorage,
            IOptions<AppSettings> appSettings,
            HtmlExtractor htmlExtractor)
        {
            _bot = bot;
            _logger = logger;
            _appSettings = appSettings.Value;
            _httpHelper = httpHelper;
            _rssParser = rssParser;
            _duplicateChecker = duplicateChecker;
            _newsStorage = newsStorage;
            _hiddenGroupId = _appSettings.HiddenGroupId;
            _htmlExtractor = htmlExtractor;
            _channels = _appSettings.Channels;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            foreach (var channel in _channels)
            {
                try
                {
                    _logger.LogInformation("🚀 شروع پردازش کانال: {Channel}", channel.Name);

                    await FetchNews(channel.Sources);
                    await PublishAsync(channel, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "⚠️ خطا در پردازش کانال {Channel}", channel.Name);
                }
            }
        }

        private async Task FetchNews(List<SourceConfig> sources)
        {
            var newsItems = new List<NewsItem>();

            foreach (var source in sources)
            {
                try
                {
                    var rssContent = await _httpHelper.GetStringAsync(source.Url);
                    newsItems.AddRange(_rssParser.Parse(rssContent));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "⚠️ خطا در واکشی منبع: {Source}", source.Url);
                }
            }

            await FilterNewsAsync(newsItems);
        }

        private Task FilterNewsAsync(List<NewsItem> items)
        {
            var footballKeywords = _appSettings.FootballKeywords;
            var nonSportKeywords = _appSettings.NonSportKeywords;

            var filtered = items
                .Where(item =>
                    !_duplicateChecker.IsDuplicateOrOld(item) &&
                    footballKeywords.Any(k => (item.Title + " " + item.ShortDescription).Contains(k)) &&
                    !nonSportKeywords.Any(k => item.Title.Contains(k) || item.ShortDescription.Contains(k)) &&
                    string.IsNullOrEmpty(item.VideoUrl))
                .OrderByDescending(i => i.PubDate)
                .Take(30)
                .ToList();

            foreach (var item in filtered)
                _newsStorage.Add(item);

            return Task.CompletedTask;
        }

        private async Task PublishAsync(ChannelConfig channelConfig, CancellationToken ct)
        {
            while (_newsStorage.Queue.Count > 0 && !ct.IsCancellationRequested)
            {
                var item = _newsStorage.Queue.Peek();

                // بررسی فاصله از آخرین پست همان منبع
                if (_lastPublishedBySource.TryGetValue(item.SourceName, out var lastTime))
                {
                    var timeSinceLast = DateTime.UtcNow - lastTime;

                    if (timeSinceLast < TimeSpan.FromMinutes(2))
                    {
                        // اگر هنوز حداقل 2 دقیقه نگذشته، منتظر بمان
                        var wait = TimeSpan.FromMinutes(2) - timeSinceLast;
                        _logger.LogDebug("⏱ انتظار {Minutes} دقیقه برای رعایت حداقل فاصله از آخرین پست منبع {Source}",
                            wait.TotalMinutes, item.SourceName);
                        await Task.Delay(wait);
                    }
                }

                item = _newsStorage.Queue.Dequeue();

                try
                {
                    // واکشی تصویر و شرح کامل
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

                    // ذخیره زمان آخرین پست از این منبع
                    _lastPublishedBySource[item.SourceName] = DateTime.UtcNow;

                    // تأخیر تصادفی بین 2 تا 20 دقیقه برای طبیعی بودن
                    var randomDelay = TimeSpan.FromMinutes(new Random().Next(2, 21));
                    _logger.LogDebug("⏱ انتظار {Minutes} دقیقه تا پست بعدی در کانال {Channel}",
                        randomDelay.TotalMinutes, channelConfig.Name);
                    await Task.Delay(randomDelay);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "⚠️ خطا در ارسال پیام به کانال {Channel}", channelConfig.Name);
                }
            }
        }

    }
}
