using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.AppUpdate.Services;
using GenHub.Features.AppUpdate.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for registering App Update module dependencies.
/// </summary>
public static class AppUpdateModule
{
    /// <summary>
    /// Registers the App Update module dependencies in the service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> with App Update module services registered.</returns>
    public static IServiceCollection AddAppUpdateModule(this IServiceCollection services)
    {
        // Register HTTP client factory for proper HttpClient lifecycle management
        services.AddHttpClient();

        // Register Velopack update manager (only update system needed)
        services.AddSingleton<IVelopackUpdateManager, VelopackUpdateManager>();

        // Register ViewModel
        services.AddTransient<UpdateNotificationViewModel>();

        return services;
    }
}