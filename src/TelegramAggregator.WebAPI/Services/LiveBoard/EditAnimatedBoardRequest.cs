using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;

namespace TelegramAggregator.WebAPI.Services.LiveBoard;

// Telegram.Bot 22.10 does not expose the Bot API's rich_message edit parameter.
internal sealed class EditAnimatedBoardRequest(long chatId, int messageId, string html)
    : RequestBase<Message>("editMessageText")
{
    private static readonly string[] EventTypes = ["goal", "redcard"];

    internal static bool HasAnimation(string html) => EventTypes.Any(type => References(html, type));

    public override HttpContent ToHttpContent()
    {
        var form = new MultipartFormDataContent();
        try
        {
            var media = new List<object>();
            foreach (var type in EventTypes.Where(type => References(html, type)))
            {
                var resource = $"TelegramAggregator.WebAPI.Services.LiveBoard.Animations.{type}.gif";
                var stream = typeof(EditAnimatedBoardRequest).Assembly.GetManifestResourceStream(resource)
                    ?? throw new InvalidOperationException($"Missing live board animation asset {type}.");
                var file = new StreamContent(stream);
                file.Headers.ContentType = new MediaTypeHeaderValue("image/gif");
                form.Add(file, type, $"{type}.gif");
                media.Add(new { id = type, media = new { type = "animation", media = $"attach://{type}" } });
            }

            form.Add(new StringContent(chatId.ToString(System.Globalization.CultureInfo.InvariantCulture)), "chat_id");
            form.Add(new StringContent(messageId.ToString(System.Globalization.CultureInfo.InvariantCulture)), "message_id");
            form.Add(new StringContent(JsonSerializer.Serialize(new { html, is_rtl = true, media }), Encoding.UTF8),
                "rich_message");
            return form;
        }
        catch
        {
            form.Dispose();
            throw;
        }
    }

    private static bool References(string html, string type) =>
        html.Contains($"<video src=\"tg://video?id={type}\">", StringComparison.Ordinal);
}
