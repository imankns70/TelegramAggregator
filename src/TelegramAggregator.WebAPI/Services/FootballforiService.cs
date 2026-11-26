using Microsoft.Extensions.Options;
using Serilog;
using System.Net;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramAggregator.WebAPI.Data;
using TelegramAggregator.WebAPI.Data.Repositories;
using TelegramAggregator.WebAPI.Infrastructure;
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Models.Entities;
using TelegramAggregator.WebAPI.Models.FootbalSettings;

namespace TelegramAggregator.WebAPI.Services
{
    public class FootballforiService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FootballforiService> _logger;
        private readonly IHttpHelperService _httpHelper;
        private readonly IRssParserService _rssParser;
        private readonly IImageExtractorService _imageExtractor;
        private readonly IFullDecriptionExtractorService _fullDecriptionExtractor;
        private readonly FootbalForiSettings _footbalForiSettings;
        private readonly IFootball360ScraperService _football360ScraperService;
        private readonly INewsRepository _newsRepository;


        public FootballforiService(
            IServiceProvider serviceProvider,
            IConfiguration configuration,
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
            _serviceProvider = serviceProvider;
            _configuration = configuration;
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
                        _logger.LogWarning("RSS خالی از {Source}", source.Name ?? source.Url);
                        continue;
                    }

                    var parsed = _rssParser.Parse(rssContent, source.Url).ToList();
                    _logger.LogInformation("Fetched {Count} recent items from {Source}", parsed.Count, source.Name ?? source.Url);
                    allItems.AddRange(parsed);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "خطا در واکشی منبع: {Source} | {Url}", source.Name ?? source.Url, source.Url);
                }
            }

            var football360News = await _football360ScraperService.GetLatestNewsAsync(ct);
            allItems.AddRange(football360News);

            if (allItems.Any())
            {
                await _newsRepository.ProcessNewsAsync(allItems, _footbalForiSettings, ct);

            }


        }
        public async Task<bool> PublishSingleNewsAsync(CancellationToken ct)
        {
            if (!_configuration.GetValue<bool>("Worker:PublishingEnabled"))
            {
                return false;
            }


            if (!FootbalForiSettings.IsPublishingChannelAllowed(_footbalForiSettings.ChannelId))
            {
                throw new InvalidOperationException("Telegram publishing requires the main or test channel.");
            }

            News news = await _newsRepository.GetBestNews(ct);
            if (news is null)
            {
                return false;
            }

            return await TrySendToTelegramAsync(news, _footbalForiSettings.ChannelId, ct);


        }
        private async Task<bool> TrySendToTelegramAsync(News item, long channelId, CancellationToken ct)
        {
            var imageTask = _imageExtractor.ExtractMainImageAsync(item.Link, ct);
            var hasVideoTask = _imageExtractor.HasVideoAsync(item.Link, ct);
            var isFootball360 = item.SportSourceId == SportSourceEnumeration.Football360.Id;
            // A body saved by an older scraper can be present but incomplete.
            var fullDescTask = isFootball360
                ? _football360ScraperService.GetFullDescriptionAsync(item.Link, ct)
                : _fullDecriptionExtractor.GetFullDescriptionAsync(item.Link, ct);
            await Task.WhenAll(imageTask, hasVideoTask, fullDescTask);

            item.ImageUrl = await imageTask;
            var hasVideo = await hasVideoTask;
            item.FullDescription = await fullDescTask;

            if (!Uri.TryCreate(item.ImageUrl, UriKind.Absolute, out var imageUrl)
                || (imageUrl.Scheme != Uri.UriSchemeHttps && imageUrl.Scheme != Uri.UriSchemeHttp))
            {
                await _newsRepository.DecayAsync(item, "خبر بدون عکس", ct, saveNow: true);
                return false;
            }
            if (hasVideo)
            {
                await _newsRepository.DecayAsync(item, "حاوی تگ ویدئو", ct, saveNow: true);
                return false;
            }
            if ((isFootball360 && string.IsNullOrWhiteSpace(item.FullDescription))
                || item.FullDescription?.Contains("خطا در دریافت محتوا") == true)
            {
                await _newsRepository.DecayAsync(item, "خطا در واکشی شرح کامل", ct, saveNow: true);
                return false;
            }

            item.Caption = CaptionMakerExtension.GetRichHtml(item);
            var richMessage = new InputRichMessage
            {
                Html = $"<img src=\"{WebUtility.HtmlEncode(imageUrl.AbsoluteUri)}\"/>\n{item.Caption}",
                IsRtl = true
            };

            var bot = _serviceProvider.GetRequiredService<ITelegramBotClient>();
            try
            {
                await bot.SendRichMessage(chatId: channelId, richMessage: richMessage, cancellationToken: ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "خطا در ارسال به تلگرام {Message}", ex.Message);
                await _newsRepository.DecayAsync(item, $"خطا در ارسال به تلگرام {ex.Message}", ct, saveNow: true);
                // A failed response can leave delivery unconfirmed, so keep the publication interval.
                return true;
            }

            try
            {
                await _newsRepository.PublishedAsync(item, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "خبر {NewsId} در تلگرام ارسال شد ولی ثبت انتشار ناموفق بود", item.Id);
            }

            return true;
        }
    }
}
