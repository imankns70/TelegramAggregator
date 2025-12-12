using TelegramAggregator.WebAPI.Data.Repositories;
using TelegramAggregator.WebAPI.Models.Repositories;

namespace TelegramAggregator.WebAPI.Data
{
    public static class DIContainer
    {
        public static IServiceCollection AddRepositories(this IServiceCollection services)
        {

            services.AddScoped<INewsRepository, NewsRepository>();
            return services;
        }
    }
}
