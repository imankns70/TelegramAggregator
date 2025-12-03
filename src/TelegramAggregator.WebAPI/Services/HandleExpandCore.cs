using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramAggregator.Services;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.WebAPI.Services
{
    public sealed class HandleExpandCore
    {
        private static readonly ConcurrentDictionary<string, ExpandData> ExpandStorage = new();
        private record ExpandData(NewsItem Item, InputFileUrl? Photo, DateTime ExpireAt);

        private readonly ITelegramBotClient _bot;
        private string _botUsername = "bot_username";

        public HandleExpandCore(ITelegramBotClient botClient)
        {
            _bot = botClient;

            _ = Task.Run(async () =>
            {
                try
                {
                    var me = await botClient.GetMe();
                    _botUsername = me.Username ?? "bot_username";
                }
                catch
                {
                    _botUsername = "bot_username";
                }
            });
        }

        // ساخت callbackId اختصاصی برای هر کاربر
        public static string StoreForExpand(NewsItem item, InputFileUrl? photo = null, long? userId = null)
        {
            var callbackId = $"exp_{Guid.NewGuid():N}";
            if (userId.HasValue)
                callbackId += $"_{userId.Value}";

            ExpandStorage[callbackId] = new ExpandData(item, photo, DateTime.UtcNow.AddHours(24));
            return callbackId;
        }

        public async Task HandleExpand(CallbackQuery cb, CancellationToken ct)
        {
            var callbackData = cb.Data!;
            var message = cb.Message!;
            var userId = cb.From.Id;
            var userName = string.IsNullOrEmpty(cb.From.FirstName) ? "کاربر" : cb.From.FirstName;

            await SafeAnswer(cb.Id, ct);

            // بررسی اینکه callback متعلق به همان کاربر است
            if (!callbackData.EndsWith($"_{userId}"))
            {
                await SafeAnswer(cb.Id, ct, "این دکمه برای شما نیست.", true);
                return;
            }

            if (!ExpandStorage.TryGetValue(callbackData, out var data))
            {
                await SafeAnswer(cb.Id, ct, "این خبر منقضی شده یا قبلاً باز شده است", true);
                return;
            }

            var item = data.Item;
            var fullCaption = MessageTemplateFormatter.GetFullCaption(item);

            bool sentPrivately = false;

            try
            {
                await _bot.SendMessage(
                    chatId: userId,
                    text: fullCaption,
                    parseMode: ParseMode.Html,
                    linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                    replyMarkup: new InlineKeyboardMarkup(
                        InlineKeyboardButton.WithUrl("بازگشت به پست اصلی",
                            $"https://t.me/{message.Chat.Username}/{message.MessageId}")),
                    cancellationToken: ct);

                sentPrivately = true;
                ExpandStorage.TryRemove(callbackData, out _);
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 403 ||
                ex.Message.Contains("bot can't send messages") ||
                ex.Message.Contains("chat not found"))
            {
                sentPrivately = false;
            }

            if (!sentPrivately)
            {
                var startLink = $"https://t.me/{_botUsername}?start={callbackData}";

                await _bot.SendMessage(
                    chatId: message.Chat.Id,
                    text: $"<a href=\"tg://user?id={userId}\">{Escape(userName)}</a> درخواست متن کامل کرد\n\n" +
                          "برای دریافت متن کامل در چت خصوصی، روی دکمه زیر کلیک کنید:",
                    parseMode: ParseMode.Html,
                    replyParameters: new ReplyParameters { MessageId = message.MessageId },
                    replyMarkup: new InlineKeyboardMarkup(
                        InlineKeyboardButton.WithUrl("دریافت متن کامل در چت خصوصی", startLink)),
                    linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                    cancellationToken: ct);

                await SafeAnswer(cb.Id, ct, "لطفاً روی دکمه زیر کلیک کنید تا متن کامل در چت خصوصی ارسال شود");
            }
            else
            {
                await SafeAnswer(cb.Id, ct, "متن کامل به چت خصوصی شما ارسال شد");
            }

            // حذف دکمه فقط برای این کاربر
            try
            {
                if (message.ReplyMarkup is InlineKeyboardMarkup markup)
                {
                    var newButtons = markup.InlineKeyboard
                        .Select(row => row.Where(btn => btn.CallbackData != callbackData).ToArray())
                        .Where(r => r.Length > 0)
                        .ToArray();

                    InlineKeyboardMarkup? updatedMarkup = newButtons.Length > 0 ? new InlineKeyboardMarkup(newButtons) : null;

                    await _bot.EditMessageReplyMarkup(
                        chatId: message.Chat.Id,
                        messageId: message.MessageId,
                        replyMarkup: updatedMarkup,
                        cancellationToken: ct);
                }
            }
            catch { }
        }

        private async Task SafeAnswer(string callbackId, CancellationToken ct, string text = "", bool alert = false)
        {
            try
            {
                await _bot.AnswerCallbackQuery(callbackId, text, alert, cancellationToken: ct);
            }
            catch { }
        }

        public void CleanupExpiredItems()
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in ExpandStorage.ToArray())
                if (kvp.Value.ExpireAt < now)
                    ExpandStorage.TryRemove(kvp.Key, out _);
        }

        private static string Escape(string text) =>
            text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
