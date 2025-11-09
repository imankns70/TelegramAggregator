using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TelegramAggregator.Core;
using TelegramAggregator.WebAPI.Core;
using TelegramAggregator.WebAPI.Models;
using TelegramAggregator.WebAPI.Models.TelegramAggregator.WebAPI.Models;

public class PublishStep : IPipelineStep
{
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<PublishStep> _logger;
    private readonly long _hiddenGroupId;

    public PublishStep(ITelegramBotClient bot, ILogger<PublishStep> logger, long hiddenGroupId)
    {
        _bot = bot;
        _logger = logger;
        _hiddenGroupId = hiddenGroupId;
    }

    public async Task ExecuteAsync(PipelineContext context)
    {
        if (context.ChannelConfig == null)
        {
            _logger.LogWarning("ChannelConfig خالی است، مرحله انتشار انجام نشد");
            return;
        }

        if (context.Queue == null || context.Queue.Count == 0)
        {
            _logger.LogInformation("صف انتشار خالی است، خبری برای ارسال وجود ندارد");
            return;
        }

        while (context.Queue.Count > 0)
        {
            var item = context.Queue.Dequeue();

            try
            {
                var text = FormatMessage(item, context.ChannelConfig);
                var caption = text.Length > 1000 ? text[..997] + "..." : text;

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
                    sentMessage = await _bot.SendMessage(_hiddenGroupId, text,
                        parseMode: ParseMode.Html, disableNotification: true);
                }

                await _bot.CopyMessage(context.ChannelConfig.ChannelId,
                    _hiddenGroupId, sentMessage.MessageId,
                    disableNotification: true);

                await _bot.DeleteMessage(_hiddenGroupId, sentMessage.MessageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "⚠️ خطا در ارسال پیام به کانال {Channel}", context.ChannelConfig.Name);
            }
        }
    }

    private string FormatMessage(NewsItem item, ChannelConfig channel)
    {
        string template = channel.Template ??
@"
{Title}

{ShortDescription}

{FullDescription}

@{ChannelUsername}
#{AdminUsername}";
        string text = template
            .Replace("{Title}", CleanText(item.Title))
            .Replace("{ShortDescription}", CleanText(item.ShortDescription))
            .Replace("{FullDescription}", CleanText(item.FullDescription))
            .Replace("{ChannelUsername}", channel.ChannelUsername)
            .Replace("{AdminUsername}", channel.AdminUsername);

        return text.Trim();
    }

    private static string CleanText(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var clean = Regex.Replace(input, "<.*?>", " ");
        clean = Regex.Replace(clean, "&[a-zA-Z]+;", " ");
        clean = Regex.Replace(clean, @"\s{2,}", " ").Trim();
        return clean;
    }
}
