using Microsoft.Extensions.DependencyInjection;

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
        /// <param name="services">The service collection to which application services will be registered.</param>
        /// <returns>The updated <see cref="IServiceCollection"/> with registered application services.</returns>
        public static IServiceCollection ConfigureApplicationServices(this IServiceCollection services)
        {
            // Register shared services here via extension modules
            services.AddGameDetectionService();
            services.AddLoggingModule();
            services.AddSharedViewModelModule();

            // Add more shared modules here as needed
            return services;
        }
    }
}
