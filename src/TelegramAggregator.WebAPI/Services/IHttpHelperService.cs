namespace TelegramAggregator.WebAPI.Services;

public interface IHttpHelperService
{
    Task<string> GetStringAsync(string url, CancellationToken ct = default);
    Task<(bool, string)> TryHtmlForCheckVideoAsync(string url, CancellationToken ct);


}