using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramAggregator.WebAPI.Models;

namespace TelegramAggregator.Services
{
    public class TelegramUpdateService : BackgroundService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly ILogger<TelegramUpdateService> _logger;

        // ذخیره موقت خبرها برای callback (24 ساعت)
        private static readonly ConcurrentDictionary<string, ExpandData> ExpandStorage = new();

        private record ExpandData(NewsItem Item, InputFileUrl? Photo, DateTime ExpireAt);

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
                new Telegram.Bot.Polling.ReceiverOptions { AllowedUpdates = new[] { UpdateType.CallbackQuery } },
                stoppingToken);

            _logger.LogInformation("TelegramUpdateService فعال شد — در انتظار کلیک روی 'ادامه خبر'");

            // پاک‌سازی خودکار هر ۶ ساعت
            _ = Task.Run(async () =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var now = DateTime.UtcNow;
                    foreach (var kvp in ExpandStorage.ToArray())
                    {
                        if (kvp.Value.ExpireAt < now)
                            ExpandStorage.TryRemove(kvp.Key, out _);
                    }
                    await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
                }
            }, stoppingToken);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
        {
            if (update.CallbackQuery?.Data?.StartsWith("exp_") == true)
                await HandleExpand(bot, update.CallbackQuery, ct);
        }

        private Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
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
                    await bot.AnswerCallbackQuery(cb.Id, "این خبر منقضی شده یا قبلاً باز شده است", true, cancellationToken: ct);
                    return;
                }

                var item = data.Item;
                var photo = data.Photo;

                // متن کامل رو بساز
                var fullCaption = MessageTemplateFormatter.GetFullCaption(item);

                if (photo != null)
                {
                    // اگر عکس داره → فقط کپشن رو ادیت کن (عکس همون قبلی می‌مونه)
                    await bot.EditMessageCaption(
                        chatId: message.Chat.Id,
                        messageId: message.MessageId,
                        caption: fullCaption,
                        parseMode: ParseMode.Html,
                        cancellationToken: ct);
                }
                else
                {
                    // اگر عکس نداره → فقط متن رو ادیت کن
                    await bot.EditMessageText(
                        chatId: message.Chat.Id,
                        messageId: message.MessageId,
                        text: fullCaption,
                        parseMode: ParseMode.Html,
                        cancellationToken: ct);
                }

                await bot.AnswerCallbackQuery(cb.Id, "متن کامل نمایش داده شد", cancellationToken: ct);
            }
            catch (ApiRequestException ex) when (ex.Message.Contains("message is not modified"))
            {
                // اگر قبلاً ادیت شده باشه، فقط جواب بده
                await bot.AnswerCallbackQuery(cb.Id, "متن کامل قبلاً نمایش داده شده", cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در نمایش متن کامل - CallbackId: {CallbackId}", callbackId);
                await bot.AnswerCallbackQuery(cb.Id, "خطایی رخ داد", true, cancellationToken: ct);
            }
        }

        public static string StoreForExpand(NewsItem item, InputFileUrl? photo = null)
        {
            var callbackId = $"{Guid.NewGuid():N}";
            ExpandStorage[callbackId] = new ExpandData(item, photo, DateTime.UtcNow.AddHours(24));
            return callbackId;
        }
    }
}