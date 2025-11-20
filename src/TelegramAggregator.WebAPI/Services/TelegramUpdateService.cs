// فایل: Services/TelegramUpdateService.cs
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramAggregator.WebAPI.Infrastructure;
using static TelegramAggregator.WebAPI.Infrastructure.DebugService;

namespace TelegramAggregator.Services
{
    public class TelegramUpdateService : BackgroundService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly ILogger<TelegramUpdateService> _logger;
        private readonly IServiceProvider _services;

        public TelegramUpdateService(ITelegramBotClient botClient, ILogger<TelegramUpdateService> logger, IServiceProvider services)
        {
            _botClient = botClient;
            _logger = logger;
            _services = services;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var receiverOptions = new ReceiverOptions()
            {
                AllowedUpdates = new[] { UpdateType.CallbackQuery, UpdateType.Message } // فقط این دو تا رو گوش بده
            };

            _botClient.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                receiverOptions,
                stoppingToken);

            _logger.LogInformation("ربات شروع به دریافت آپدیت کرد...");
            await Task.Delay(Timeout.Infinite, stoppingToken); // تا ابد زنده بمونه
        }

        // اینجا همه پیام‌ها و کلیک‌ها میان
        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
        {
            // فقط به کلیک روی دکمه‌ها (CallbackQuery) واکنش نشون بده
            if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery?.Data != null)
            {
                await HandleCallbackQuery(botClient, update.CallbackQuery, ct);
            }
        }

        // خطاها رو بگیر که ربات هنگ نکنه
        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken ct)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiEx => $"خطای تلگرام: {apiEx.ErrorCode} - {apiEx.Message}",
                _ => exception.ToString()
            };
            _logger.LogError(exception, errorMessage);
            return Task.CompletedTask;
        }

        // قلب تپنده! اینجا کلیک روی "نمایش ادامه خبر" رو هندل می‌کنیم
        private async Task HandleCallbackQuery(ITelegramBotClient botClient, CallbackQuery callback, CancellationToken ct)
        {
            var data = callback.Data!;

            try
            {
                if (data.StartsWith("expand_"))
                {
                    // استخراج هش از دیتا
                    var hashString = data["expand_".Length..];
                    if (!int.TryParse(hashString, out int hash)) return;

                    // پیدا کردن خبر از DebugService (آخرین ۱۵۰ تا رو داره)
                    var debugItem = DebugService.GetRecent()
                        .FirstOrDefault(x => Math.Abs(x.Link.GetHashCode()) == hash);

                    if (debugItem == null)
                    {
                        await botClient.AnswerCallbackQuery(callback.Id, "خبر یافت نشد یا منقضی شده", true, cancellationToken: ct);
                        return;
                    }
                    var newsItem = debugItem.ToNewsItem();
                    // متن کامل + فوتر
                    var fullCaption = MessageTemplateFormatter.GetFullCaption(newsItem);

                    // دکمه جدید
                    var keyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("بستن متن کامل", "collapse") },
                        new[] { InlineKeyboardButton.WithUrl("مشاهده خبر کامل", newsItem.Link!) }
                    });

                    // ویرایش همون پیام
                    await botClient.EditMessageCaption(
                        chatId: callback.Message!.Chat.Id,
                        messageId: callback.Message.MessageId,
                        caption: fullCaption,
                        replyMarkup: keyboard,
                        cancellationToken: ct);

                    // یه تیک کوچیک نشون بده
                  await  botClient.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا در هندل کردن Collapse");
               await botClient.AnswerCallbackQuery(callback.Id, "خطایی رخ داد", true, cancellationToken: ct);
            }
        }
    }
}