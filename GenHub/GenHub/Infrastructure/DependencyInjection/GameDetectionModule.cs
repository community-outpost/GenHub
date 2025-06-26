using Microsoft.Extensions.DependencyInjection;
using GenHub.Services;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class GameDetectionServiceModule
    {
        public static IServiceCollection AddGameDetectionService(this IServiceCollection services)
        {
            services.AddSingleton<GameDetectionService>();
            return services;
        }
    }
}
