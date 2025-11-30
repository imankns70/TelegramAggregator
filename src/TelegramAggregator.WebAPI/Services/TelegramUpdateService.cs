using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.Services
{
    public sealed class TelegramUpdateService : BackgroundService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly ILogger<TelegramUpdateService> _logger;
        private static readonly ConcurrentDictionary<string, ExpandData> ExpandStorage = new();
        private record ExpandData(NewsItem Item, InputFileUrl? Photo, DateTime ExpireAt);

        public TelegramUpdateService(ITelegramBotClient botClient, ILogger<TelegramUpdateService> logger)
        {
            _botClient = botClient;
            _logger = logger;

            // پاک‌سازی دوره‌ای آیتم‌های منقضی شده
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        var now = DateTime.UtcNow;
                        foreach (var kvp in ExpandStorage.ToArray())
                            if (kvp.Value.ExpireAt < now)
                                ExpandStorage.TryRemove(kvp.Key, out _);

                        await Task.Delay(TimeSpan.FromHours(6));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "خطا در پاک‌سازی ExpandStorage");
                        await Task.Delay(TimeSpan.FromMinutes(10));
                    }
                }
            });
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _botClient.StartReceiving(
                        HandleUpdateAsync,
                        HandleErrorAsync,
                        new Telegram.Bot.Polling.ReceiverOptions
                        {
                            AllowedUpdates = [UpdateType.CallbackQuery, UpdateType.Message],
                            DropPendingUpdates = true
                        },
                        stoppingToken);

                    _logger.LogInformation("TelegramUpdateService فعال شد — در انتظار کلیک یا /start");
                    await Task.Delay(Timeout.Infinite, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogCritical(ex, "StartReceiving کرش کرد — 10 ثانیه دیگه دوباره تلاش می‌کنم...");
                    await Task.Delay(10_000, stoppingToken);
                }
            }
        }

        private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
        {
            // 1. هندل کردن کلیک روی دکمه "متن کامل خبر"
            if (update.CallbackQuery?.Data?.StartsWith("exp_") == true)
            {
                _ = Task.Run(async () => await SafeHandleExpand(bot, update.CallbackQuery!, ct), ct);
                return;
            }

            // 2. هندل کردن دستور /start expand_... (وقتی کاربر از دکمه زیر پست میاد)
            if (update.Message?.Text?.StartsWith("/start expand_") == true)
            {
                var payload = update.Message.Text["/start expand_".Length..].Trim();
                var key = $"exp_{payload}";

                if (ExpandStorage.TryRemove(key, out var data))
                {
                    var fullCaption = MessageTemplateFormatter.GetFullCaption(data.Item);
                    await bot.SendMessage(
                        chatId: update.Message.Chat.Id,
                        text: fullCaption,
                        parseMode: ParseMode.Html,
                        linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                        cancellationToken: ct);
                }
                else
                {
                    await bot.SendMessage(
                        chatId: update.Message.Chat.Id,
                        text: "این خبر منقضی شده یا قبلاً باز شده است.",
                        cancellationToken: ct);
                }
            }
        }

        private async Task SafeHandleExpand(ITelegramBotClient bot, CallbackQuery cb, CancellationToken ct)
        {
            try
            {
                await HandleExpandCore(bot, cb, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطای غیرمنتظره در HandleExpand — User: {UserId}, Data: {Data}",
                    cb.From.Id, cb.Data);
            }
        }

        private async Task HandleExpandCore(ITelegramBotClient bot, CallbackQuery cb, CancellationToken ct)
        {
            var callbackId = cb.Data!;
            var message = cb.Message!;
            var userId = cb.From.Id;
            var userName = string.IsNullOrEmpty(cb.From.FirstName) ? "کاربر" : cb.From.FirstName;

            await SafeAnswerCallback(bot, cb.Id);

            if (!ExpandStorage.TryGetValue(callbackId, out var data))
            {
                await SafeAnswerCallback(bot, cb.Id, "این خبر منقضی شده یا قبلاً باز شده است", true, ct);
                return;
            }

            var item = data.Item;
            var fullCaption = MessageTemplateFormatter.GetFullCaption(item);
            var botInfo = await bot.GetMe(ct);
            var botUsername = botInfo.Username!;

            bool sentPrivately = false;

            // مرحله ۱: تلاش برای ارسال به چت خصوصی
            try
            {
                await bot.SendMessage(
                    chatId: userId,
                    text: fullCaption,
                    parseMode: ParseMode.Html,
                    linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                    replyMarkup: new InlineKeyboardMarkup(
                        InlineKeyboardButton.WithUrl("بازگشت به پست",
                            $"https://t.me/{message.Chat.Username}/{message.MessageId}")),
                    cancellationToken: ct);

                sentPrivately = true;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 403 || ex.Message.Contains("bot can't send messages"))
            {
                sentPrivately = false;
            }

            // مرحله ۲: اگر نشد خصوصی بفرستیم → کاربر رو راهنمایی کنیم
            if (!sentPrivately)
            {
                var payload = callbackId["exp_".Length..];
                var startLink = $"https://t.me/{botUsername}?start=expand_{payload}";

                await bot.SendMessage(
                    chatId: message.Chat.Id,
                    text: $"<a href=\"tg://user?id={userId}\">{Escape(userName)}</a> درخواست متن کامل کرد\n\n" +
                          "برای دریافت متن کامل در چت خصوصی، روی دکمه زیر کلیک کنید:",
                    parseMode: ParseMode.Html,
                    replyParameters: new ReplyParameters { MessageId = message.MessageId },
                    replyMarkup: new InlineKeyboardMarkup(
                        InlineKeyboardButton.WithUrl("دریافت متن کامل در چت خصوصی", startLink)),
                    linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                    cancellationToken: ct);

                // داده رو حذف نکنیم تا وقتی کاربر با /start expand_ بیاد
                await SafeAnswerCallback(bot, cb.Id, "لطفاً روی دکمه زیر کلیک کنید تا متن کامل در چت خصوصی بیاید");
            }
            else
            {
                // اگر موفق شدیم خصوصی بفرستیم → داده رو حذف کنیم
                ExpandStorage.TryRemove(callbackId, out _);
                await SafeAnswerCallback(bot, cb.Id, "متن کامل به چت خصوصی شما ارسال شد");
            }

            // مرحله ۳: فقط دکمه این کاربر حذف بشه (بقیه هنوز داشته باشن)
            try
            {
                await bot.EditMessageReplyMarkup(
                    chatId: message.Chat.Id,
                    messageId: message.MessageId,
                    replyMarkup: null,
                    cancellationToken: ct);
            }
            catch { /* مهم نیست */ }
        }

        private static async Task SafeAnswerCallback(ITelegramBotClient bot, string callbackId,
            string text = "", bool showAlert = false, CancellationToken ct = default)
        {
            try
            {
                await bot.AnswerCallbackQuery(callbackId, text, showAlert, cancellationToken: ct);
            }
            catch { }
        }

        private Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
        {
            _logger.LogError(ex, "خطا در دریافت آپدیت تلگرام");
            return Task.CompletedTask;
        }

        public static string StoreForExpand(NewsItem item, InputFileUrl? photo = null)
        {
            var callbackId = $"exp_{Guid.NewGuid():N}";
            ExpandStorage[callbackId] = new ExpandData(item, photo, DateTime.UtcNow.AddHours(24));
            return callbackId;
        }

        private static string Escape(string text) =>
            text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}