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
            services.AddScoped<IFotMobScraperService, FotMobScraperService>();
            services.AddScoped<ILiveBoardMessageFormatter, LiveBoardMessageFormatter>();
            return services;
        }
    }
}
