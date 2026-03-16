using GenHub.Core.Interfaces.Services;
using GenHub.Features.Tools.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for UploadThing services.
/// </summary>
public static class UploadThingModule
{
    /// <summary>
    /// Registers UploadThing services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddUploadThingServices(this IServiceCollection services)
    {
        services.AddSingleton<IUploadThingService, UploadThingService>();

        return services;
    }
}
