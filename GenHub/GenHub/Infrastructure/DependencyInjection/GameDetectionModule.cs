using GenHub.Core;
using GenHub.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection
{
    /// <summary>
    /// Provides extension methods for registering game detection services.
    /// </summary>
    public static class GameDetectionModule
    {
        /// <summary>
        /// Registers the <see cref="GameDetectionService"/> as a singleton in the service collection.
        /// </summary>
        /// <param name="services">The service collection to add the service to.</param>
        /// <returns>The updated service collection.</returns>
        public static IServiceCollection AddGameDetectionService(this IServiceCollection services)
        {
            services.AddSingleton<GameDetectionService>();
            services.AddScoped<IGameDetector, DummyGameDetector>();
            return services;
        }
    }
}
