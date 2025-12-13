using System;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Manifest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for registering manifest generation services.
/// </summary>
/// <remarks>
/// This module registers all services required for content manifest generation,
/// caching, and management following the dependency injection patterns.
/// </remarks>
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

        // Manifest provider with proper factory pattern
        services.AddScoped<IManifestProvider>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<ManifestProvider>>();
            var manifestPool = provider.GetRequiredService<IContentManifestPool>();
            var manifestIdService = provider.GetService<IManifestIdService>();
            var manifestBuilder = provider.GetService<IContentManifestBuilder>();
            var options = new ManifestProviderOptions
            {
                GenerateFallbackManifests = false,
            };

            // Use correct constructor signature for ManifestProvider
            return new ManifestProvider(logger, manifestPool, manifestIdService, manifestBuilder, options);
        });

        services.AddSingleton<IManifestIdService, ManifestIdService>();

        // Discovery and generation services
        services.AddScoped<ManifestDiscoveryService>();

        services.AddScoped<IManifestGenerationService, ManifestGenerationService>();

        services.AddTransient<IContentManifestBuilder, ContentManifestBuilder>();

        // Register factory function for creating transient manifest builders
        // This allows resolvers to get fresh builder instances without injecting IServiceProvider
        services.AddTransient<Func<IContentManifestBuilder>>(provider =>
            () => provider.GetRequiredService<IContentManifestBuilder>());

        // Startup service
        services.AddHostedService<ManifestInitializationService>();

        return services;
    }
}
