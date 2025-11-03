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
    private readonly Random _random = new();
    private readonly HashSet<string> _sentLinks = new();
    private readonly Queue<string> _linkOrder = new();
    private readonly TimeSpan _minPoll = TimeSpan.FromMinutes(18);
    private readonly TimeSpan _maxPoll = TimeSpan.FromMinutes(25);
    private const int MaxStoredLinks = 1000;

    public AggregatorService(IConfiguration config, ILogger<AggregatorService> logger, IOptions<AppSettings> appSettings)
    {
        _bot = new TelegramBotClient(config["BotToken"] ?? throw new ArgumentNullException("BotToken"));
        _logger = logger;
        _channels = config.GetSection("Channels").Get<List<ChannelConfig>>() ?? throw new InvalidOperationException("Channels config missing");
        _hiddenGroupId = long.Parse(config["HiddenGroupId"] ?? throw new ArgumentNullException("HiddenGroupId"));
        _appSettings = appSettings;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.Now;

            // خواب شبانه: 1 تا 8 صبح
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

            // چرخه اصلی بعدی: 10 تا 15 دقیقه رندوم
            var mainDelay = TimeSpan.FromMinutes(_random.Next(10, 16));
            _logger.LogInformation("🕒 تأخیر بعدی چرخه اصلی: {Minutes} دقیقه", mainDelay.TotalMinutes);
            await Task.Delay(mainDelay, ct);
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

                // تعداد خبر رندوم برای این چرخه: 1 تا 6
                var candidates = filtered
                    .Where(i => !_sentLinks.Contains(i.Link))
                    .OrderByDescending(i => i.PubDate)
                    .Take(_random.Next(1, 7))
                    .ToList();

                foreach (var item in candidates)
                {
                    await SendMessage(channel, item, ct);
                    TrackLink(item);


                    var innerDelay = TimeSpan.FromSeconds(_random.Next(30, 181));
                    _logger.LogDebug("⏱ انتظار {Seconds} ثانیه تا خبر بعدی", innerDelay.TotalSeconds);
                    await Task.Delay(innerDelay, ct);
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

                foreach (var entry in feed.Items.Take(20))
                {
                    var title = entry.Title?.Text ?? "";
                    var description = entry.Summary?.Text ?? "";

                    // نادیده گرفتن خبرهایی که به عکس یا ویدئو اشاره دارند
                    if (!IsTitleAllowed(title))
                        continue;

                    var item = new ContentItem
                    {
                        Title = CleanText(title),
                        Description = CleanText(description),
                        Link = entry.Links.FirstOrDefault()?.Uri?.ToString() ?? "",
                        PubDate = entry.PublishDate.DateTime
                    };

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

    private List<ContentItem> FilterItems(List<ContentItem> items)
    {
        var footballKeywords = _appSettings.Value.FootballKeywords;
        var nonSportKeywords = _appSettings.Value.NonSportKeywords;
        var forbiddenTitleKeywords = _appSettings.Value.ForbiddenTitleKeywords;

        return items
       .Where(i =>
           !i.Title.Contains("ویدئو") &&
           !i.Description.Contains("ویدئو") &&
           !i.Title.Contains("عکس") &&
           !i.Description.Contains("عکس") &&
           !forbiddenTitleKeywords.Any(k => i.Title.Contains(k) || i.Description.Contains(k)) &&
           !nonSportKeywords.Any(k => i.Title.Contains(k) || i.Description.Contains(k)) &&
           footballKeywords.Any(k => (i.Title + " " + i.Description).Contains(k))
       )
       .OrderByDescending(i => i.PubDate)
       .Take(20)
       .ToList();
    }

    private async Task SendMessage(ChannelConfig channel, ContentItem item, CancellationToken ct)
    {
        try
        {
            var text = FormatMessage(item, channel);

            // محدودیت caption در تلگرام ~ 1024 کاراکتر (برای ایمنی ما 1000)
            string caption = text.Length > 1000 ? text.Substring(0, 1000 - 3) + "..." : text;

            Message sentMessage;

            if (!string.IsNullOrEmpty(item.VideoUrl))
            {
                _logger.LogDebug("ارسال ویدیو برای کانال {Channel}: {Title}", channel.Name, item.Title);
                sentMessage = await _bot.SendVideo(
                    chatId: _hiddenGroupId,
                    video: item.VideoUrl,
                    caption: caption,
                    parseMode: ParseMode.Html,
                    disableNotification: true,
                    cancellationToken: ct
                );
            }
            else if (!string.IsNullOrEmpty(item.ImageUrl))
            {
                _logger.LogDebug("ارسال عکس برای کانال {Channel}: {Title}", channel.Name, item.Title);
                sentMessage = await _bot.SendPhoto(
                    chatId: _hiddenGroupId,
                    photo: item.ImageUrl,
                    caption: caption,
                    parseMode: ParseMode.Html,
                    disableNotification: true,
                    cancellationToken: ct
                );
            }
            else
            {
                _logger.LogDebug("ارسال متن برای کانال {Channel}: {Title}", channel.Name, item.Title);
                sentMessage = await _bot.SendMessage(
                    chatId: _hiddenGroupId,
                    text: text,
                    parseMode: ParseMode.Html,
                    disableNotification: true,
                    cancellationToken: ct
                );
            }

            // کپی به کانال و حذف از گروه مخفی
            await _bot.CopyMessage(
                chatId: channel.ChannelId,
                fromChatId: _hiddenGroupId,
                messageId: sentMessage.MessageId,
                disableNotification: true,
                cancellationToken: ct
            );

            // حذف پیام ارسالی از گروه مخفی تا فقط کانال دیده شود
            await _bot.DeleteMessage(_hiddenGroupId, sentMessage.MessageId, ct);

            _logger.LogInformation("ارسال {Type} به کانال {Channel}: {Title}",
                item.HasMedia ? (item.VideoUrl != null ? "ویدیو" : "عکس") : "متن",
                channel.Name,
                item.Title.Length > 50 ? item.Title.Substring(0, 50) + "..." : item.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطا در ارسال به کانال {Channel}", channel.Name);
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
        var clean = Regex.Replace(input, "<.*?>", " "); // حذف تگ‌ها
        clean = Regex.Replace(clean, "&[a-zA-Z]+;", " "); // حذف &nbsp; و غیره
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
    private bool IsTitleAllowed(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return true;

        title = title.Trim().ToLowerInvariant();
        return !ForbiddenTitleKeywords.Any(k => title.Contains(k));
    }
}
