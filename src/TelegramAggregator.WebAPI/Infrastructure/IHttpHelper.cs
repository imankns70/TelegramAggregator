namespace TelegramAggregator.WebAPI.Infrastructure;

public interface IHttpHelper
{
    Task<string?> GetStringAsync(string url, CancellationToken ct = default);
 
}