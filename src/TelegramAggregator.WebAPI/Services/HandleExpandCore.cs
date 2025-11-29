using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramAggregator.Services;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Services
{
    public class HandleExpandCore
    {
        private static readonly ConcurrentDictionary<string, ExpandData> ExpandStorage = new();
        private record ExpandData(NewsItem Item, InputFileUrl? Photo, DateTime ExpireAt);
        private readonly ITelegramBotClient _bot;

        public HandleExpandCore(ITelegramBotClient botClient)
        {
            _bot = botClient;
        }

        public async Task HandleExpand(CallbackQuery cb, CancellationToken ct)
        {
            try
            {
                await SafeAnswer(cb.Id, ct);

                if (!ExpandStorage.TryRemove(cb.Data!, out var data))
                {
                    await SafeAnswer(cb.Id, ct, "این خبر منقضی شده است", true);
                    return;
                }

                var caption = MessageTemplateFormatter.GetFullCaption(data.Item);

                await _bot.SendMessage(
                    chatId: cb.Message!.Chat.Id,
                    text: caption,
                    parseMode: ParseMode.Html,
                    replyParameters: new ReplyParameters { MessageId = cb.Message.MessageId },
                    linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                    cancellationToken: ct);

                await _bot.EditMessageCaption(
                    chatId: cb.Message.Chat.Id,
                    messageId: cb.Message.MessageId,
                    caption: "☑️ متن کامل ارسال شد",
                    replyMarkup: null,
                    parseMode: ParseMode.Html,
                    cancellationToken: ct);

                await SafeAnswer(cb.Id, ct, "متن کامل ارسال شد");
            }
            catch (Exception ex)
            {
               
                await SafeAnswer(cb.Id, ct, "خطا رخ داد", true);
            }
        }

        private async Task SafeAnswer(string callbackId, CancellationToken ct, string text = "", bool alert = false)
        {
            try { await _bot.AnswerCallbackQuery(callbackId, text, alert, cancellationToken: ct); }
            catch { }
        }
        public void CleanupExpiredItems()
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in ExpandStorage.ToArray())
                if (kvp.Value.ExpireAt < now)
                    ExpandStorage.TryRemove(kvp.Key, out _);
        }

        public static string StoreForExpand(NewsItem item, InputFileUrl? photo = null)
        {
            var callbackId = $"exp_{Guid.NewGuid():N}";
            ExpandStorage[callbackId] = new ExpandData(item, photo, DateTime.UtcNow.AddHours(24));
            return callbackId;
        }
    }
}
