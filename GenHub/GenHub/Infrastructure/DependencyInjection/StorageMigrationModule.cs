using GenHub.Common.Services;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for storage and installation migration services.
/// </summary>
public static class StorageMigrationModule
{
    /// <summary>
    /// Registers storage migration services with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddStorageMigrationServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IStorageMigrationService, StorageMigrationService>();
        services.TryAddSingleton<IInstallationLocationTracker, NullInstallationLocationTracker>();
        return services;
    }
}
