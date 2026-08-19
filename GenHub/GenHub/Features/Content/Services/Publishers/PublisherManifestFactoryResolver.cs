using System.Collections.Generic;
using System.Linq;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Resolves the appropriate publisher-specific manifest factory for a given manifest.
/// </summary>
public class PublisherManifestFactoryResolver(IEnumerable<IPublisherManifestFactory> factories, ILogger<PublisherManifestFactoryResolver> logger)
{
    /// <summary>
    /// Resolves the appropriate factory for the given manifest.
    /// </summary>
    /// <param name="manifest">The manifest to resolve a factory for.</param>
    /// <returns>The appropriate publisher-specific factory, or null if no factory can handle the manifest.</returns>
    public IPublisherManifestFactory? ResolveFactory(ContentManifest manifest)
    {
        // Try to find a specialized factory that can handle this manifest
        var factory = factories.FirstOrDefault(f => f.CanHandle(manifest));

        if (factory != null)
        {
            logger.LogInformation(
                "Resolved {FactoryType} for manifest {ManifestId} (Publisher: {Publisher})",
                factory.GetType().Name,
                manifest.Id,
                manifest.Publisher?.PublisherType ?? GameClientConstants.UnknownVersion);
            return factory;
        }

        // Fallback to generic GitHub manifest factory for non-GameClient publisher content or generic releases
        var fallbackFactory = factories.OfType<GitHubManifestFactory>().FirstOrDefault();
        if (fallbackFactory != null)
        {
            logger.LogInformation(
                "Resolved fallback {FactoryType} for manifest {ManifestId} (Publisher: {Publisher}, ContentType: {ContentType})",
                fallbackFactory.GetType().Name,
                manifest.Id,
                manifest.Publisher?.PublisherType ?? GameClientConstants.UnknownVersion,
                manifest.ContentType);
            return fallbackFactory;
        }

        logger.LogWarning(
            "No factory found for manifest {ManifestId} (Publisher: {Publisher}, ContentType: {ContentType})",
            manifest.Id,
            manifest.Publisher?.PublisherType ?? GameClientConstants.UnknownVersion,
            manifest.ContentType);

        return null;
    }
}
