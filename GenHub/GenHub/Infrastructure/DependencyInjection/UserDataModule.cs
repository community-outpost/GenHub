using GenHub.Core.Interfaces.UserData;
using GenHub.Features.UserData.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for user data management services.
/// These services handle content installation to the user's Documents folder
/// (maps, replays, etc.) using hard links from the CAS.
/// </summary>
public static class UserDataModule
{
    /// <summary>
    /// Registers user data services with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddUserDataServices(this IServiceCollection services)
    {
        // UserDataTrackerService is scoped - handles individual file tracking operations
        // It interacts with CAS and file system for hard-linking content to user directories
        services.AddScoped<IUserDataTracker, UserDataTrackerService>();

        // ProfileContentLinkerService is scoped - orchestrates user data content per profile
        // It coordinates with UserDataTracker to manage content when profiles are switched
        services.AddScoped<IProfileContentLinker, ProfileContentLinkerService>();

        return services;
    }
}
