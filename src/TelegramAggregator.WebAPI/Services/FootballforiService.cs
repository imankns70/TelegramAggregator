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
    public class FootballforiService
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
                    await Task.Delay(3000, ct);
                }
            }

            var football360News = await _football360ScraperService.GetLatestNewsAsync(ct);
            allItems.AddRange(football360News);

            if (allItems.Any())
            {
                await _newsRepository.ProcessNewsAsync(allItems, _footbalForiSettings, ct);

            }


        }
        public async Task PublishSingleNewsAsync(CancellationToken ct)
        {
            _logger.LogInformation("PublishContinuouslyAsync شروع شد — تا ابد زنده می‌مونه");


            News news = await _newsRepository.GetBestNews(ct);
            if (news is null)
            {
                await Task.Delay(3000, ct);

            }
            else
            {
                await SendToTelegramAsync(news, _footbalForiSettings.ChannelId, ct);

            }


        }
        private async Task SendToTelegramAsync(News item, long channelId, CancellationToken ct)
        {
            bool hasVideo = false;

            var imageTask = _imageExtractor.ExtractMainImageAsync(item.Link, ct);
            var hasVideoTask = _imageExtractor.HasVideoAsync(item.Link, ct);

            Task<string> fullDescTask = null;

            if (item.SportSourceId != SportSourceEnumeration.Football360.Id)
            {
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
                await _newsRepository.DecayAsync(item, "خبر بدون عکس", ct, saveNow: true);
                await Task.Delay(3000, ct);
            }

            else if (hasVideo)
            {

                await _newsRepository.DecayAsync(item, "حاوی تگ ویدئو", ct, saveNow: true);
                await Task.Delay(3000, ct);

            }
            else if (item.FullDescription.Contains("خطا در دریافت محتوا"))
            {
                await _newsRepository.DecayAsync(item, "خطا در واکشی شرح کامل", ct, saveNow: true);
                await Task.Delay(3000, ct);
            }
            else
            {


                try
                {
                    var caption = CaptionMakerExtension.GetCaption(item);
                    item.Caption = caption;

                    //Message sentMessage;

                    //sentMessage = await _bot.SendPhoto(
                    //    chatId: channelId, // 👈 مستقیماً کانال
                    //    photo: new InputFileUrl(item.ImageUrl),
                    //    caption: caption,
                    //    parseMode: ParseMode.Html,
                    //    cancellationToken: ct);

                    await _newsRepository.PublishedAsync(item, ct);

                }
                catch (Exception ex)
                {
                    _logger.LogError($"خطا در ارسال به تلگرام {ex.Message}");
                    await _newsRepository.DecayAsync(item, $"خطا در ارسال به تلگرام {ex.Message}", ct, saveNow: true);
                    await Task.Delay(TimeSpan.FromSeconds(3000), ct);

                }


            }

        }
    }
}