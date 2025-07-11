using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Main module that orchestrates registration of all application services.
/// </summary>
public static class AppServices
{
    /// <summary>
    /// Registers all core application services and modules required for GenHub.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The <see cref="IServiceCollection"/> with all GenHub services registered.</returns>
    public static IServiceCollection ConfigureApplicationServices(this IServiceCollection services)
    {
        // Register shared services via extension modules
        services.AddGameDetectionService();
        services.AddLoggingModule();
        services.AddSharedViewModelModule();

        // Register additional shared modules as needed
        return services;
    }
}
