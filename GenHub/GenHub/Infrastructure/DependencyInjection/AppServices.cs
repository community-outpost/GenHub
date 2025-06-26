using Microsoft.Extensions.DependencyInjection;
using GenHub.Services;
using GenHub.ViewModels;
using GenHub.Core;

namespace GenHub.Infrastructure.DependencyInjection
{
    /// <summary>
    /// Main module that orchestrates registration of all application services.
    /// </summary>
    public static class AppServices
    {
        /// <summary>
        /// Registers all shared services (non-platform-specific).
        /// </summary>
        public static IServiceCollection ConfigureApplicationServices(this IServiceCollection services)
        {
            // Register shared services here via extension modules
            services.AddGameDetectionService();
            services.AddSharedViewModelModule();
            // Add more shared modules here as needed
            return services;
        }
    }
}
