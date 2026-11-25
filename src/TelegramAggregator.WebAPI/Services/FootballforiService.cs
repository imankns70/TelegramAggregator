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
            // RSS fetching remains disabled, as in the local working copy.

            var football360News = await _football360ScraperService.GetLatestNewsAsync(ct);
            allItems.AddRange(football360News);

            if (allItems.Any())
            {
                await _newsRepository.ProcessNewsAsync(allItems, _footbalForiSettings, ct);

            }


        }
        public async Task PublishSingleNewsAsync(CancellationToken ct)
        {
            if (!_configuration.GetValue<bool>("Worker:PublishingEnabled"))
            {
                return;
            }


            if (_footbalForiSettings.ChannelId != FootbalForiSettings.TestChannelId)
            {
                throw new InvalidOperationException("Telegram publishing is restricted to the test channel.");
            }

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
                fullDescTask = _fullDecriptionExtractor.GetFullDescriptionAsync(item.Link, ct);
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

            if (!Uri.TryCreate(item.ImageUrl, UriKind.Absolute, out var imageUrl)
                || (imageUrl.Scheme != Uri.UriSchemeHttps && imageUrl.Scheme != Uri.UriSchemeHttp))
            {
                await _newsRepository.DecayAsync(item, "خبر بدون عکس", ct, saveNow: true);
                await Task.Delay(3000, ct);
            }
            else if (hasVideo)
            {
                await _newsRepository.DecayAsync(item, "حاوی تگ ویدئو", ct, saveNow: true);
                await Task.Delay(3000, ct);
            }
            else if (item.FullDescription != null && item.FullDescription.Contains("خطا در دریافت محتوا"))
            {
                await _newsRepository.DecayAsync(item, "خطا در واکشی شرح کامل", ct, saveNow: true);
                await Task.Delay(3000, ct);
            }
            else
            {


                try
                {
                    item.Caption = CaptionMakerExtension.GetRichHtml(item);
                    var richMessage = new InputRichMessage
                    {
                        Html = $"<img src=\"{WebUtility.HtmlEncode(imageUrl.AbsoluteUri)}\"/>\n{item.Caption}",
                        IsRtl = true
                    };

                    var bot = _serviceProvider.GetRequiredService<ITelegramBotClient>();
                    await bot.SendRichMessage(
                        chatId: channelId,
                        richMessage: richMessage,
                        cancellationToken: ct);

                    await _newsRepository.PublishedAsync(item, ct);

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "خطا در ارسال به تلگرام {Message}", ex.Message);
                    await _newsRepository.DecayAsync(item, $"خطا در ارسال به تلگرام {ex.Message}", ct, saveNow: true);
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);  // was 3000s (50 min!) — fixed to 3s
                }


            }

        }
    }
}
