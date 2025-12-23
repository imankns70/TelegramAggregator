using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramAggregator.WebAPI.Data.Repositories;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Models.Entities;
using TelegramAggregator.WebAPI.Models.FootbalSettings;

namespace TelegramAggregator.WebAPI.Services
{
    public partial class FootballforiService
    {
        private readonly ITelegramBotClient _bot;
        private readonly ILogger<FootballforiService> _logger;
        private readonly IHttpHelperService _httpHelper;
        private readonly IRssParserService _rssParser;
        private readonly IImageExtractorService _imageExtractor;
        private readonly IFullDecriptionExtractorService _fullDecriptionExtractor;
        private readonly FootbalForiSettings _footbalForiSettings;
        private readonly IFootball360ScraperService _football360ScraperService;
        private readonly INewsRepository _newsRepository;
     

        public FootballforiService(
            ITelegramBotClient bot,
            ILogger<FootballforiService> logger,
            IHttpHelperService httpHelper,
            IRssParserService rssParser,
            IOptions<AppSettings> appSettings,
            IImageExtractorService imageExtractor,
            IFullDecriptionExtractorService fullDecriptionExtractor,
            IHostEnvironment environment,
            IFootball360ScraperService football360ScraperService,
            INewsRepository newsRepository)
        {
            _bot = bot;
            _logger = logger;
            _footbalForiSettings = appSettings.Value.FootbalForiSettings;
            _httpHelper = httpHelper;
            _rssParser = rssParser;
            _imageExtractor = imageExtractor;
            _fullDecriptionExtractor = fullDecriptionExtractor;
            _football360ScraperService = football360ScraperService;
            _newsRepository = newsRepository;
        }


        public async Task FetchAndFilterNewsAndAddToQueueAsync(CancellationToken ct)
        {
            var allItems = new List<NewsItem>();
            foreach (var source in _footbalForiSettings.Sources)
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


            await _newsRepository.ProcessNewsAsync(allItems, _footbalForiSettings, ct);


        }
        public async Task<bool> PublishSingleNewsAsync(CancellationToken ct)
        {
            News news = await _newsRepository.GetBestNews(ct);
            if (news == null)
            {
                _logger.LogInformation("هیچ خبری برای انتشار آماده نیست.");
                return false;
            }

            try
            {
                await SendToTelegramAsync(news, _footbalForiSettings.ChannelId, ct);
                _logger.LogInformation("خبر با موفقیت منتشر شد: {Title}", news.Title);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در انتشار خبر: {Title}", news.Title);
                // در صورت خطا، خبر رو decay نکن — شاید بعداً دوباره امتحان بشه
                return false;
            }
        }

        private async Task SendToTelegramAsync(News item, long channelId, CancellationToken ct)
        {
            var imageTask = _imageExtractor.ExtractMainImageAsync(item.Link, ct);
            var hasVideoTask = _imageExtractor.HasVideoAsync(item.Link, ct);
            Task<string> fullDescTask = null;

            if (item.SportSourceId != SportSourceEnumeration.Football360.Id)
            {
                fullDescTask = _fullDecriptionExtractor.GetFullDescriptionAsync(item.Link);
                await Task.WhenAll(imageTask, hasVideoTask, fullDescTask);
                item.FullDescription = await fullDescTask;
            }
            else
            {
                await Task.WhenAll(imageTask, hasVideoTask);
            }

            item.ImageUrl = await imageTask;
            bool hasVideo = await hasVideoTask;

            // فیلترهای رد خبر
            if (string.IsNullOrEmpty(item.ImageUrl))
            {
                await _newsRepository.DecayAsync(item, "خبر بدون عکس", ct, saveNow: true);
                return;
            }

            if (hasVideo)
            {
                await _newsRepository.DecayAsync(item, "حاوی تگ ویدئو", ct, saveNow: true);
                return;
            }

            if (item.FullDescription?.Contains("خطا در دریافت محتوا") == true)
            {
                await _newsRepository.DecayAsync(item, "خطا در واکشی شرح کامل", ct, saveNow: true);
                return;
            }

            // ارسال موفق
            var caption = CaptionMakerExtension.GetCaption(item);
            item.Caption = caption;
            await _newsRepository.PublishedAsync(item, ct);

            Message sentMessage;
            if (!string.IsNullOrEmpty(item.ImageUrl))
            {
                sentMessage = await _bot.SendPhoto(
                    chatId: channelId,
                    photo: new InputFileUrl(item.ImageUrl),
                    caption: caption,
                    parseMode: ParseMode.Html,
                    cancellationToken: ct);
            }
            else
            {
                sentMessage = await _bot.SendMessage(
                    chatId: channelId,
                    text: caption,
                    parseMode: ParseMode.Html,
                    cancellationToken: ct);
            }

            _logger.LogInformation("خبر ارسال شد به کانال | MessageId: {MessageId} | عنوان: {Title}",
                sentMessage.MessageId, item.Title);
        }
    }
}