namespace GenHub.Infrastructure.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using GenHub.Common.ViewModels;

/// <summary>
/// Provides extension methods for registering shared ViewModels in the dependency injection container.
/// </summary>
public static class SharedViewModelModule
{
    /// <summary>
    /// Adds shared ViewModels to the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharedViewModelModule(this IServiceCollection services)
    {
        // Register MainViewModel (critical for app startup)
        services.AddSingleton<MainViewModel>();
        return services;
    }
}
