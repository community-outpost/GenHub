using GenHub.Common.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for shared view models.
/// </summary>
public static class SharedViewModelModule
{
    /// <summary>
    /// Adds shared view model services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharedViewModelModule(this IServiceCollection services)
    {
        // Register ViewModels
        services.AddTransient<MainViewModel>();

        return services;
    }
}
