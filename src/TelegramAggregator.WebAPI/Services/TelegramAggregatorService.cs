// AggregatorService.cs (نسخه نهایی)
using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Services;

public class AggregatorService : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<AggregatorService> _logger;
    private readonly List<ChannelConfig> _channels;
    private readonly Dictionary<string, string> _intros;
    private readonly TimeSpan _pollInterval;
    private readonly HashSet<string> _sentLinks = new();
    private readonly Queue<string> _linkOrder = new();
    private const int MaxStoredLinks = 500;
    private readonly long _hiddenGroupId;

    public AggregatorService(IConfiguration config, ILogger<AggregatorService> logger)
    {
        _bot = new TelegramBotClient(config["BotToken"] ?? throw new ArgumentNullException("BotToken"));
        _logger = logger;
        _channels = config.GetSection("Channels").Get<List<ChannelConfig>>() ?? throw new InvalidOperationException("Channels config missing");
        _intros = config.GetSection("Intros:Keywords").Get<Dictionary<string, string>>() ?? new();
        _hiddenGroupId = long.Parse(config["HiddenGroupId"] ?? throw new ArgumentNullException("HiddenGroupId"));
        _pollInterval = TimeSpan.FromMinutes(int.Parse(config["PollIntervalMinutes"] ?? "20"));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("ربات شروع شد. Poll هر {Minutes} دقیقه.", _pollInterval.TotalMinutes);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessAllChannels(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در چرخه اصلی");
            }

            await Task.Delay(_pollInterval, ct);
        }
    }

    public async Task ProcessAllChannels(CancellationToken ct)
    {
        foreach (var channel in _channels)
        {
            try
            {
                var items = await FetchItems(channel);
                if (!items.Any())
                {
                    _logger.LogDebug("هیچ آیتمی برای {Channel} پیدا نشد", channel.Name);
                    continue;
                }

                var filtered = FilterItems(items, channel);
                foreach (var item in filtered)
                {
                    var message = FormatMessage(item, channel);
                    if (IsDuplicate(message)) continue;

                    await SendMessage(channel, message, ct);
                    TrackLink(message);
                    await Task.Delay(1000, ct); // جلوگیری از Rate Limit
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در پردازش کانال {ChannelName}", channel.Name);
            }
        }
    }

    private async Task<List<ContentItem>> FetchItems(ChannelConfig channel)
    {
        var items = new List<ContentItem>();
        foreach (var source in channel.Sources.Where(s => s.Type.Equals("Rss", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using var reader = XmlReader.Create(source.Url, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
                var feed = SyndicationFeed.Load(reader);
                foreach (var entry in feed.Items.Take(20)) // حداکثر ۲۰ تا برای سرعت
                {
                    items.Add(new ContentItem
                    {
                        Title = entry.Title?.Text?.Trim() ?? "بدون عنوان",
                        Description = (entry.Summary?.Text ?? entry.Content?.ToString() ?? "").Trim(),
                        Link = entry.Links.FirstOrDefault()?.Uri?.ToString() ?? "",
                        PubDate = entry.PublishDate.DateTime
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "خطا در خواندن RSS: {Url}", source.Url);
            }
        }
        return items;
    }

    private List<ContentItem> FilterItems(List<ContentItem> items, ChannelConfig channel)
    {
        // 90% فوتبال
        var footballKeywords = new[] { "فوتبال", "لیگ", "دربی", "استقلال", "پرسپولیس", "تیم ملی", "چمپیونز", "جام جهانی" };
        var footballItems = items
            .Where(i => footballKeywords.Any(k => i.Title.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .Take(9)
            .ToList();

        // 10% ورزش‌های مهم دیگر
        var otherKeywords = new[] { "والیبال", "فوتسال", "کشتی", "وزنه‌برداری" };
        var importantOther = items
            .Where(i => otherKeywords.Any(k => i.Title.Contains(k, StringComparison.OrdinalIgnoreCase)) &&
                        (i.Title.Contains("قهرمان") || i.Title.Contains("طلا") || i.Title.Contains("مدال")))
            .Take(1)
            .ToList();

        return footballItems
            .Concat(importantOther)
            .OrderByDescending(i => i.PubDate)
            .ToList();
    }

    private string FormatMessage(ContentItem item, ChannelConfig channel)
    {
        var intro = GetIntro(item.Title, channel);
        var adminSign = string.IsNullOrEmpty(channel.AdminUsername) ? "" : $"\n\nاز: @{channel.AdminUsername}";

        var template = channel.Template ?? "{Intro}\n\n{Title}\n\n{Description}\n\n{AdminSign}\n\n#ورزش | ساعت {Time}";

        return template
            .Replace("{Intro}", intro)
            .Replace("{Title}", Escape(item.Title))
            .Replace("{Description}", Escape(item.Description))
            .Replace("{Time}", item.PubDate.ToLocalTime().ToString("HH:mm"))
            .Replace("{AdminSign}", adminSign);
    }

    private string GetIntro(string title, ChannelConfig channel)
    {
        title = title.ToLower();
        foreach (var kvp in _intros)
        {
            if (title.Contains(kvp.Key.ToLower()))
                return kvp.Value;
        }
        return "سلام رفیق! یه خبر مهم";
    }

    private bool IsDuplicate(string message)
    {
        var link = ExtractLink(message);
        return !string.IsNullOrEmpty(link) && _sentLinks.Contains(link);
    }

    private void TrackLink(string message)
    {
        var link = ExtractLink(message);
        if (string.IsNullOrEmpty(link)) return;

        _sentLinks.Add(link);
        _linkOrder.Enqueue(link);
        while (_linkOrder.Count > MaxStoredLinks)
            _sentLinks.Remove(_linkOrder.Dequeue());
    }

    private string? ExtractLink(string message) =>
        Regex.Match(message, @"https?://[^\s""]+", RegexOptions.IgnoreCase)?.Value;

    private async Task SendMessage(ChannelConfig channel, string text, CancellationToken ct)
    {
        try
        {
            var sentMessage = await _bot.SendMessage(
                chatId: _hiddenGroupId,
                text: text,
                parseMode: ParseMode.Html,
                disableNotification: true,
                cancellationToken: ct
            );

            await _bot.CopyMessage(
                chatId: channel.ChannelId,
                fromChatId: _hiddenGroupId,
                messageId: sentMessage.MessageId,
                parseMode: ParseMode.Html,
                disableNotification: true,
                cancellationToken: ct
            );

            _logger.LogInformation("ارسال شد به {Channel}: {Title}", channel.Name, text.Substring(0, Math.Min(50, text.Length)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطا در ارسال به کانال {Channel}", channel.Name);
        }
    }

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}