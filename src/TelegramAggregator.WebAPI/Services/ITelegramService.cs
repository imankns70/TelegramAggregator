using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
namespace TelegramAggregator.WebAPI.Services;

public interface ITelegramService
{
    Task<Message> SendMessageAsync(long chatId, string text, ParseMode parseMode = ParseMode.Html, CancellationToken ct = default);

    Task<Message> SendPhotoAsync(long chatId, string photoUrl, string? caption = null, ParseMode parseMode = ParseMode.Html, CancellationToken ct = default);

    Task<Message> SendVideoAsync(long chatId, string videoUrl, string? caption = null, ParseMode parseMode = ParseMode.Html, CancellationToken ct = default);

    Task CopyMessageAsync(long targetChatId, long sourceChatId, int messageId, CancellationToken ct = default);

    Task DeleteMessageAsync(long chatId, int messageId, CancellationToken ct = default);

    Task<Message?> SendHiddenMessageAsync(string text, string? imageUrl = null, string? videoUrl = null, CancellationToken ct = default);
}
