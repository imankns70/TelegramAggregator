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
        public FootballforiService(ITelegramBotClient bot, ILogger<FootballforiService> logger,
            HttpHelper httpHelper,
            RssParser rssParser, DuplicateChecker duplicateChecker,
            NewsStorage newsStorage, IOptions<AppSettings> appSettings, HtmlExtractor htmlExtractor)
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

                    await PublishAsync(channel);


                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "⚠️ خطا در پردازش کانال {Channel}", channel.Name);
                }
            }
        }

        public async Task FetchNews(List<SourceConfig> sources)
        {
            List<NewsItem> newsItems = new List<NewsItem>();
            try
            {
                foreach (var source in sources)
                {
                    // واکشی محتوا
                    var rssContent = await _httpHelper.GetStringAsync(source.Url);

                    // پارس RSS و تبدیل به NewsItem
                    newsItems.AddRange(_rssParser.Parse(rssContent));

                }

                await FilterNewsAsync(newsItems);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در واکشی و پارس RSS:", ex.Message);
            }

            await Task.CompletedTask;
        }

        private Task FilterNewsAsync(List<NewsItem> items)
        {
            var footballKeywords = _appSettings.FootballKeywords;
            var nonSportKeywords = _appSettings.NonSportKeywords;

            var filtered = items
                .Where(item =>
                   !_duplicateChecker.IsDuplicateOrOld(item) &&      // حذف تکراری‌ها و قدیمی‌ها
                    footballKeywords.Any(k => (item.Title + " " + item.ShortDescription).Contains(k)) &&
                    !nonSportKeywords.Any(k => item.Title.Contains(k) || item.ShortDescription.Contains(k)) &&
                    string.IsNullOrEmpty(item.VideoUrl)
                )
                .OrderByDescending(i => i.PubDate)
                .Take(30)
                .ToList();

            // اضافه کردن اخبار غیرتکراری به NewsStorage (ذخیره + صف انتشار)
            foreach (var item in filtered)
            {
                _newsStorage.Add(item);
            }
            return Task.CompletedTask;
        }

        private async Task PublishAsync(ChannelConfig channelConfig)
        {
            while (_newsStorage.Queue.Count > 0)
            {
                var item = _newsStorage.Queue.Dequeue();

                try
                {


                    // 🔹 واکشی تصویر و متن کامل قبل از ساخت پیام
                    var imageTask = _htmlExtractor.ExtractMainImageAsync(item.Link);
                    var fullDescTask = _htmlExtractor.GetFullDescriptionAsync(item.Link);
                    await Task.WhenAll(imageTask, fullDescTask);

                    item.ImageUrl = imageTask.Result;
                    item.FullDescription = fullDescTask.Result;


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

                    // 🕒 اضافه کردن تاخیر بین ارسال هر خبر
                    var delay = TimeSpan.FromSeconds(new Random().Next(30, 90));
                    _logger.LogDebug("⏱ انتظار {Seconds} ثانیه تا خبر بعدی در کانال {Channel}",
                        delay.TotalSeconds, channelConfig.Name);
                    await Task.Delay(delay);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "⚠️ خطا در ارسال پیام به کانال {Channel}", channelConfig.Name);
                }
            }

        }





    }
}
