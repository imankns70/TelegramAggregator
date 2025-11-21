// Services/TelegramUpdateService.cs
using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;    // این حتماً باشه!
using Telegram.Bot.Types.ReplyMarkups;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.Services
{
    public class TelegramUpdateService : BackgroundService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly ILogger<TelegramUpdateService> _logger;

        // ذخیره موقت خبر + عکس (برای کلیک بعدی)
        private static readonly ConcurrentDictionary<string, ExpandData> ExpandStorage = new();

        private record ExpandData(NewsItem Item, InputFileUrl Photo, DateTime ExpireAt);

        public TelegramUpdateService(ITelegramBotClient botClient, ILogger<TelegramUpdateService> logger)
        {
            _botClient = botClient;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _botClient.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                new ReceiverOptions { AllowedUpdates = new[] { UpdateType.CallbackQuery } },
                stoppingToken);

            _logger.LogInformation("TelegramUpdateService فعال شد — فقط کلیک روی دکمه");

            // پاک‌سازی خودکار هر ۶ ساعت
            _ = Task.Run(async () =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var now = DateTime.UtcNow;
                    foreach (var kvp in ExpandStorage)
                    {
                        if (kvp.Value.ExpireAt < now)
                            ExpandStorage.TryRemove(kvp.Key, out _);
                    }
                    await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
                }
            }, stoppingToken);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
        {
            if (update.CallbackQuery?.Data?.StartsWith("exp_") == true)
                await HandleExpand(botClient, update.CallbackQuery!, ct);
        }

        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception ex, CancellationToken ct)
        {
            _logger.LogError(ex, "خطا در دریافت آپدیت تلگرام");
            return Task.CompletedTask;
        }

        private async Task HandleExpand(ITelegramBotClient bot, CallbackQuery cb, CancellationToken ct)
        {
            var callbackId = cb.Data!;
            var message = cb.Message!;

            try
            {
                if (!ExpandStorage.TryRemove(callbackId, out var data))
                {
                    await bot.AnswerCallbackQuery(cb.Id, "خبر منقضی شده یا قبلاً باز شده", true);
                    return;
                }

                var (item, photo, _) = data;

                // استفاده از متد جدید با HTML و دکمه کو چیک کن
                var (fullCaption, keyboard) = MessageTemplateFormatter.GetFinalMessage(item);

                await bot.EditMessageMedia(
                    chatId: message.Chat.Id,
                    messageId: message.MessageId,
                    media: new InputMediaPhoto(photo)
                    {
                        Caption = fullCaption,
                        ParseMode = ParseMode.Html           // HTML فعال
                    },
                    replyMarkup: keyboard,                   // دکمه کو چیک کن از Formatter میاد
                    cancellationToken: ct
                );

                await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در باز کردن خبر کامل - CallbackId: {CallbackId}", callbackId);
                await bot.AnswerCallbackQuery(cb.Id, "خطایی رخ داد", true);
            }
        }

        // متد استاتیک برای ذخیره خبر موقع ارسال اولیه
        public static string StoreForExpand(NewsItem item, InputFileUrl photo)
        {
            var callbackId = $"exp_{Math.Abs(item.Link.GetHashCode()):X8}";
            ExpandStorage[callbackId] = new ExpandData(item, photo, DateTime.UtcNow.AddHours(24));
            return callbackId;
        }
    }
}