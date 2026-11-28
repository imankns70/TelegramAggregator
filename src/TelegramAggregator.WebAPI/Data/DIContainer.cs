using TelegramAggregator.WebAPI.Data.Repositories;
using TelegramAggregator.WebAPI.Models.Repositories;
using TelegramAggregator.WebAPI.Services.LiveBoard;

namespace TelegramAggregator.WebAPI.Data
{
    public static class DIContainer
    {
        public static IServiceCollection AddRepositories(this IServiceCollection services)
        {
            services.AddScoped<INewsRepository, NewsRepository>();
            services.AddScoped<ILiveBoardRepository, LiveBoardRepository>();
            services.AddHttpClient<IFotMobScraperService, FotMobScraperService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(12);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
            }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All
            });
            services.AddScoped<ILiveBoardMessageFormatter, LiveBoardMessageFormatter>();
            return services;
        }
    }
}
