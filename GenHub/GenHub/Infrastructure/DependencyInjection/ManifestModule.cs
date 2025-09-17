using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Manifest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for registering manifest generation services.
/// </summary>
public static class ManifestModule
{
    /// <summary>
    /// Registers manifest generation services for dependency injection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddManifestServices(this IServiceCollection services)
    {
        // Core manifest services
        services.AddSingleton<IManifestCache, ManifestCache>();
        services.AddScoped<IManifestProvider, ManifestProvider>();
        services.AddSingleton<IManifestIdService>(new ManifestIdService());

        // Discovery and generation services
        services.AddScoped<ManifestDiscoveryService>();
        services.AddSingleton<IManifestGenerationService, ManifestGenerationService>();
        services.AddTransient<IContentManifestBuilder, ContentManifestBuilder>();

        // Startup service
        services.AddHostedService<ManifestInitializationService>();

        return services;
    }
}
