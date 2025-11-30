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

            // یکبار یوزرنیم ربات رو می‌گیریم (برای لینک ?start=expand_)
            _ = Task.Run(async () =>
            {
                try
                {
                    var me = await botClient.GetMe();
                    _botUsername = me.Username ?? "ot_username"; // اگر یوزرنیم نداره، دستی وارد کن
                }
                catch
                {
                    _botUsername = "bot_username"; // در صورت خطا، دستی تنظیم کن
                }
            });
        }

        public async Task HandleExpand(CallbackQuery cb, CancellationToken ct)
        {
            var callbackId = cb.Data!;
            var message = cb.Message!;
            var userId = cb.From.Id;
            var userName = string.IsNullOrEmpty(cb.From.FirstName) ? "کاربر" : cb.From.FirstName;

            // فوراً لودینگ رو ببند
            await SafeAnswer(cb.Id, ct);

            // اگر داده وجود نداشت یا قبلاً استفاده شده
            if (!ExpandStorage.TryGetValue(callbackId, out var data))
            {
                await SafeAnswer(cb.Id, ct, "این خبر منقضی شده یا قبلاً باز شده است", true);
                return;
            }

            var item = data.Item;
            var fullCaption = MessageTemplateFormatter.GetFullCaption(item);

            bool sentPrivately = false;

            // مرحله ۱: تلاش برای ارسال به چت خصوصی
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
                ExpandStorage.TryRemove(callbackId, out _); // فقط اگر موفق بود حذف کن
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 403 ||
                ex.Message.Contains("bot can't send messages") ||
                ex.Message.Contains("chat not found"))
            {
                sentPrivately = false;
            }

            // مرحله ۲: اگر نشد خصوصی بفرستیم → راهنمایی عمومی با دکمه مستقیم
            if (!sentPrivately)
            {
                var payload = callbackId["exp_".Length..];
                var startLink = $"https://t.me/{_botUsername}?start=expand_{payload}";

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

            // مرحله ۳: فقط دکمه این کاربر حذف بشه (بقیه هنوز داشته باشن!)
            try
            {
                await _bot.EditMessageReplyMarkup(
                    chatId: message.Chat.Id,
                    messageId: message.MessageId,
                    replyMarkup: null,
                    cancellationToken: ct);
            }
            catch { /* مهم نیست — تلگرام خودش مدیریت می‌کنه */ }
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

        public static string StoreForExpand(NewsItem item, InputFileUrl? photo = null)
        {
            var callbackId = $"exp_{Guid.NewGuid():N}";
            ExpandStorage[callbackId] = new ExpandData(item, photo, DateTime.UtcNow.AddHours(24));
            return callbackId;
        }

        // برای جلوگیری از خطای HTML در تگ کردن کاربر
        private static string Escape(string text) =>
            text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}