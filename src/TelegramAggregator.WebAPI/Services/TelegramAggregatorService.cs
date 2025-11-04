using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Services;

public class AggregatorService : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<AggregatorService> _logger;
    private readonly List<ChannelConfig> _channels;
    private readonly long _hiddenGroupId;
    private readonly IOptions<AppSettings> _appSettings;
    private readonly HttpClient _http;
    private readonly Random _random = new();
    private readonly HashSet<string> _sentLinks = new();
    private readonly Queue<string> _linkOrder = new();
    private const int MaxStoredLinks = 1000;

    public AggregatorService(IConfiguration config, ILogger<AggregatorService> logger, IOptions<AppSettings> appSettings)
    {
        _bot = new TelegramBotClient(config["BotToken"] ?? throw new ArgumentNullException("BotToken"));
        _logger = logger;
        _channels = config.GetSection("Channels").Get<List<ChannelConfig>>() ?? throw new InvalidOperationException("Channels config missing");
        _hiddenGroupId = long.Parse(config["HiddenGroupId"] ?? throw new ArgumentNullException("HiddenGroupId"));
        _appSettings = appSettings;
        _http = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) };
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.Now;

            if (now.Hour >= 1 && now.Hour < 8)
            {
                _logger.LogInformation("⏸ در حالت خواب شبانه تا ساعت ۸ صبح...");
                await Task.Delay(TimeSpan.FromMinutes(30), ct);
                continue;
            }

            try
            {
                await ProcessAllChannels(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ خطا در چرخه اصلی");
            }

            var delay = TimeSpan.FromMinutes(_random.Next(10, 16));
            _logger.LogInformation("🕒 تاخیر تا چرخه بعدی: {Minutes} دقیقه", delay.TotalMinutes);
            await Task.Delay(delay, ct);
        }
    }

    private async Task ProcessAllChannels(CancellationToken ct)
    {
        foreach (var channel in _channels)
        {
            try
            {
                _logger.LogInformation("🚀 شروع پردازش کانال: {Channel}", channel.Name);

                var items = await FetchItems(channel.Sources);
                if (!items.Any()) continue;

                var filtered = FilterItems(items);
                if (!filtered.Any()) continue;

                var candidates = filtered
                    .Where(i => !_sentLinks.Contains(i.Link))
                    .OrderByDescending(i => i.PubDate)
                    .Take(_random.Next(1, 6))
                    .ToList();

                foreach (var item in candidates)
                {
                    await SendMessage(channel, item, ct);
                    TrackLink(item);

                    var delay = TimeSpan.FromSeconds(_random.Next(30, 90));
                    _logger.LogDebug("⏱ انتظار {Seconds} ثانیه تا خبر بعدی", delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "⚠️ خطا در پردازش کانال {Channel}", channel.Name);
            }
        }
    }

    private async Task<List<ContentItem>> FetchItems(List<SourceConfig> sources)
    {
        var items = new List<ContentItem>();

        foreach (var source in sources.Where(s => s.Type == "Rss"))
        {
            try
            {
                using var reader = XmlReader.Create(source.Url);
                var feed = SyndicationFeed.Load(reader);

                foreach (var entry in feed.Items.Take(5))
                {
                    var item = new ContentItem
                    {
                        Title = CleanText(entry.Title?.Text ?? ""),
                        Description = CleanText(entry.Summary?.Text ?? ""),
                        Link = entry.Links.FirstOrDefault()?.Uri?.ToString() ?? "",
                        PubDate = entry.PublishDate.DateTime
                    };

                    await EnrichMediaAsync(item);

                    // ⚠️ اگر ویدیوی blob دارد، این خبر را نادیده بگیر
                    if (!string.IsNullOrEmpty(item.VideoUrl) && item.VideoUrl.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("⛔ حذف خبر دارای blob video: {Link}", item.Link);
                        continue;
                    }

                    items.Add(item);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ خطا در خواندن RSS: {Url}", source.Url);
            }
        }

        return items;
    }

    private async Task EnrichMediaAsync(ContentItem item)
    {
        if (string.IsNullOrEmpty(item.Link)) return;

        try
        {
            var html = await _http.GetStringAsync(item.Link);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // عکس‌ها
            var imgNode = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']") ??
                          doc.DocumentNode.SelectSingleNode("//img[contains(@src,'news') or contains(@src,'picture')]");
            if (imgNode != null)
            {
                var imgUrl = imgNode.GetAttributeValue("content", null) ?? imgNode.GetAttributeValue("src", null);
                if (!string.IsNullOrWhiteSpace(imgUrl))
                    item.ImageUrl = NormalizeUrl(item.Link, imgUrl);
            }

            // ویدیوها
            var videoNode = doc.DocumentNode.SelectSingleNode("//meta[@property='og:video']") ??
                            doc.DocumentNode.SelectSingleNode("//video/source[@src]") ??
                            doc.DocumentNode.SelectSingleNode("//video[@src]");
            if (videoNode != null)
            {
                var videoUrl = videoNode.GetAttributeValue("content", null) ?? videoNode.GetAttributeValue("src", null);
                if (!string.IsNullOrWhiteSpace(videoUrl))
                    item.VideoUrl = NormalizeUrl(item.Link, videoUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "❗ خطا در واکشی رسانه از {Link}", item.Link);
        }
    }

    private static string NormalizeUrl(string baseUrl, string mediaUrl)
    {
        if (mediaUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return mediaUrl;
        try
        {
            var baseUri = new Uri(baseUrl);
            return new Uri(baseUri, mediaUrl).ToString();
        }
        catch
        {
            return mediaUrl;
        }
    }

    private List<ContentItem> FilterItems(List<ContentItem> items)
    {
        var footballKeywords = _appSettings.Value.FootballKeywords;
        var nonSportKeywords = _appSettings.Value.NonSportKeywords;

        return items
            .Where(i =>
                footballKeywords.Any(k => (i.Title + " " + i.Description).Contains(k)) &&
                !nonSportKeywords.Any(k => i.Title.Contains(k) || i.Description.Contains(k))
            )
            .OrderByDescending(i => i.PubDate)
            .Take(30)
            .ToList();
    }

    private async Task SendMessage(ChannelConfig channel, ContentItem item, CancellationToken ct)
    {
        try
        {
            var text = FormatMessage(item, channel);
            var caption = text.Length > 1000 ? text[..997] + "..." : text;

            Message sentMessage;

            if (!string.IsNullOrEmpty(item.VideoUrl))
            {
                sentMessage = await _bot.SendVideo(_hiddenGroupId, item.VideoUrl, caption: caption, parseMode: ParseMode.Html, disableNotification: true, cancellationToken: ct);
            }
            else if (!string.IsNullOrEmpty(item.ImageUrl))
            {
                sentMessage = await _bot.SendPhoto(_hiddenGroupId, item.ImageUrl, caption: caption, parseMode: ParseMode.Html, disableNotification: true, cancellationToken: ct);
            }
            else
            {
                sentMessage = await _bot.SendMessage(_hiddenGroupId, text, parseMode: ParseMode.Html, disableNotification: true, cancellationToken: ct);
            }

            await _bot.CopyMessage(channel.ChannelId, _hiddenGroupId, sentMessage.MessageId, disableNotification: true, cancellationToken: ct);
            await _bot.DeleteMessage(_hiddenGroupId, sentMessage.MessageId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ خطا در ارسال پیام به کانال {Channel}", channel.Name);
        }
    }

    private string FormatMessage(ContentItem item, ChannelConfig channel)
    {
        string text = channel.Template ?? "{Title}\n\n{Description}\n\n@{ChannelUsername}\n#{AdminUsername}";
        text = text.Replace("{Title}", CleanText(item.Title))
                   .Replace("{Description}", CleanText(item.Description))
                   .Replace("{ChannelUsername}", channel.ChannelUsername)
                   .Replace("{AdminUsername}", channel.AdminUsername);
        return text;
    }

    private static string CleanText(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var clean = Regex.Replace(input, "<.*?>", " ");
        clean = Regex.Replace(clean, "&[a-zA-Z]+;", " ");
        clean = Regex.Replace(clean, @"\s{2,}", " ").Trim();
        return clean;
    }

    private void TrackLink(ContentItem item)
    {
        if (string.IsNullOrEmpty(item.Link)) return;

        _sentLinks.Add(item.Link);
        _linkOrder.Enqueue(item.Link);

        while (_linkOrder.Count > MaxStoredLinks)
        {
            var old = _linkOrder.Dequeue();
            _sentLinks.Remove(old);
        }
    }
}
