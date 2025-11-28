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
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // حتی اگه StartReceiving کرش کرد، دوباره شروع بشه
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _botClient.StartReceiving(
                        HandleUpdateAsync,
                        HandleErrorAsync,
                        new Telegram.Bot.Polling.ReceiverOptions {
                            AllowedUpdates = [UpdateType.CallbackQuery],                             
                            DropPendingUpdates = true
                        },
                        stoppingToken);

                    _logger.LogInformation("TelegramUpdateService فعال شد — در انتظار کلیک روی 'ادامه خبر'");
                    await Task.Delay(Timeout.Infinite, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogCritical(ex, "StartReceiving کرش کرد — 10 ثانیه دیگه دوباره تلاش می‌کنم...");
                    await Task.Delay(10_000, stoppingToken);
                }
            }
        }

        // پاک‌سازی خودکار — حتی اگه خطا بده، سرویس می‌مونه
        public TelegramUpdateService()
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        var now = DateTime.UtcNow;
                        foreach (var kvp in ExpandStorage.ToArray())
                        {
                            if (kvp.Value.ExpireAt < now)
                                ExpandStorage.TryRemove(kvp.Key, out _);
                        }
                        await Task.Delay(TimeSpan.FromHours(6));
                    }
                    catch (Exception ex)
                    {
                        // حتی اگه این حلقه کرش کنه، سرویس نمیمیره
                        _logger.LogError(ex, "خطا در پاک‌سازی ExpandStorage — ادامه می‌دم...");
                        await Task.Delay(TimeSpan.FromMinutes(10));
                    }
                }
            });
        }

        private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
        {
            if (update.CallbackQuery?.Data?.StartsWith("exp_") != true) return;

            _ = Task.Run(async () => await SafeHandleExpand(bot, update.CallbackQuery!, ct), ct);
        }

        private async Task SafeHandleExpand(ITelegramBotClient bot, CallbackQuery cb, CancellationToken ct)
        {
            try
            {
                await HandleExpandCore(bot, cb, ct);
            }
            catch (Exception ex)
            {
                // هیچ استثنایی نباید از اینجا بیرون بیاد — برنامه هرگز کرش نمی‌کنه
                _logger.LogError(ex, "خطای غیرمنتظره در HandleExpand — User: {UserId}, Callback: {Data}",
                    cb.From.Id, cb.Data);
            }
        }

        private async Task HandleExpandCore(ITelegramBotClient bot, CallbackQuery cb, CancellationToken ct)
        {
            var callbackId = cb.Data!;
            var message = cb.Message!;

            // لودینگ فوراً تموم بشه — حتی اگه بقیه کارها خطا بده
            await SafeAnswerCallback(bot, cb.Id);

            try
            {
                if (!ExpandStorage.TryRemove(callbackId, out var data))
                {
                    await SafeAnswerCallback(bot, cb.Id, "این خبر منقضی شده یا قبلاً باز شده است", true, ct);
                    return;
                }

                var item = data.Item;
                var photoUrl = item.ImageUrl;
                var fullCaption = MessageTemplateFormatter.GetFullCaption(item);

                if (!string.IsNullOrEmpty(photoUrl))
                {
                    await bot.EditMessageCaption(
                        chatId: message.Chat.Id,
                        messageId: message.MessageId,
                        caption: fullCaption,
                        parseMode: ParseMode.Html,
                        cancellationToken: ct);
                }
                else
                {
                    await bot.EditMessageText(
                        chatId: message.Chat.Id,
                        messageId: message.MessageId,
                        text: fullCaption,
                        parseMode: ParseMode.Html,
                        cancellationToken: ct);
                }

                await SafeAnswerCallback(bot, cb.Id, "متن کامل نمایش داده شد");
            }
            catch (ApiRequestException ex) when (
                ex.Message.Contains("message is not modified") ||
                ex.Message.Contains("query is too old") ||
                ex.Message.Contains("response timeout expired") ||
                ex.Message.Contains("query ID is invalid") ||
                ex.Message.Contains("message to edit not found"))
            {
                await SafeAnswerCallback(bot, cb.Id, "متن کامل قبلاً نمایش داده شده یا منقضی شده است");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در نمایش متن کامل — User: {UserId}, Callback: {Data}", cb.From.Id, callbackId);
                await SafeAnswerCallback(bot, cb.Id, "خطایی رخ داد", true, ct);
            }
        }

        private static async Task SafeAnswerCallback(ITelegramBotClient bot, string callbackId,
            string text = "", bool showAlert = false, CancellationToken ct = default)
        {
            try
            {
                await bot.AnswerCallbackQuery(callbackId, text, showAlert, cancellationToken: ct);
            }
            catch { /* هیچوقت کرش نکن — حتی اگه Answer هم خطا بده */ }
        }

        private Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
        {
            // حتی اینم کرش نکنه
            try
            {
                _logger.LogError(ex, "خطا در دریافت آپدیت تلگرام");
            }
            catch { /* عمداً خالی — برنامه هرگز نمی‌میره */ }
            return Task.CompletedTask;
        }

        public static string StoreForExpand(NewsItem item, InputFileUrl? photo = null)
        {
            var callbackId = $"exp_{Guid.NewGuid():N}";
            ExpandStorage[callbackId] = new ExpandData(item, photo, DateTime.UtcNow.AddHours(24));
            return callbackId;
        }
    }
}