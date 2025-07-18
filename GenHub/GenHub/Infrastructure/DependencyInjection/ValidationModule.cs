using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Validation;
using GenHub.Features.Manifest;
using GenHub.Features.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for registering validation-related services.
/// </summary>
public static class ValidationModule
{
    /// <summary>
    /// Registers validation services for dependency injection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddValidationServices(this IServiceCollection services)
    {
        services.AddSingleton<IGameVersionValidator, GameVersionValidator>();
        services.AddSingleton<IGameInstallationValidator, GameInstallationValidator>();
        return services;
    }
}
