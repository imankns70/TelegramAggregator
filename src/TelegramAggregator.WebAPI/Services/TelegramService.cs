using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types;
namespace TelegramAggregator.WebAPI.Services;

public class TelegramService: ITelegramService
{
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<TelegramService> _logger;

    private readonly long _hiddenGroupId;

    public TelegramService(ITelegramBotClient bot, ILogger<TelegramService> logger, long hiddenGroupId)
    {
        _bot = bot;
        _logger = logger;
        _hiddenGroupId = hiddenGroupId;
    }

    public async Task<Message> SendMessageAsync(long chatId, string text, ParseMode parseMode = ParseMode.Html, CancellationToken ct = default)
    {
        return await _bot.SendMessage(
            chatId: chatId,
            text: text,
            parseMode: parseMode,
            disableNotification: true,
            cancellationToken: ct);
    }

    public async Task<Message> SendPhotoAsync(long chatId, string photoUrl, string? caption = null, ParseMode parseMode = ParseMode.Html, CancellationToken ct = default)
    {
        return await _bot.SendPhoto(
            chatId: chatId,
            photo: photoUrl,
            caption: caption,
            parseMode: parseMode,
            disableNotification: true,
            cancellationToken: ct);
    }

    public async Task<Message> SendVideoAsync(long chatId, string videoUrl, string? caption = null, ParseMode parseMode = ParseMode.Html, CancellationToken ct = default)
    {
        return await _bot.SendVideo(
            chatId: chatId,
            video: videoUrl,
            caption: caption,
            parseMode: parseMode,
            disableNotification: true,
            cancellationToken: ct);
    }

    public async Task CopyMessageAsync(long targetChatId, long sourceChatId, int messageId, CancellationToken ct = default)
    {
        await _bot.CopyMessage(
            chatId: targetChatId,
            fromChatId: sourceChatId,
            messageId: messageId,
            disableNotification: true,
            cancellationToken: ct);
    }

    public async Task DeleteMessageAsync(long chatId, int messageId, CancellationToken ct = default)
    {
        try
        {
            await _bot.DeleteMessage(chatId, messageId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ خطا در حذف پیام موقت از گروه پنهان ({ChatId})", chatId);
        }
    }

    public async Task<Message?> SendHiddenMessageAsync(string text, string? imageUrl = null, string? videoUrl = null, CancellationToken ct = default)
    {
        try
        {
            if (!string.IsNullOrEmpty(videoUrl))
            {
                return await SendVideoAsync(_hiddenGroupId, videoUrl, text, ParseMode.Html, ct);
            }
            else if (!string.IsNullOrEmpty(imageUrl))
            {
                return await SendPhotoAsync(_hiddenGroupId, imageUrl, text, ParseMode.Html, ct);
            }
            else
            {
                return await SendMessageAsync(_hiddenGroupId, text, ParseMode.Html, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطا در ارسال پیام به گروه پنهان");
            return null;
        }
    }
}
