using GenHub.Common.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Registers shared ViewModels for DI.
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
