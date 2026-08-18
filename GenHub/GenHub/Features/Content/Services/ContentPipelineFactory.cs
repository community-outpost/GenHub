using System;
using System.Collections.Generic;
using System.Linq;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Providers;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Factory for obtaining content pipeline components by provider ID.
/// Matches the providerId from JSON configuration to registered components.
/// </summary>
/// <param name="discoverers">All registered content discoverers.</param>
/// <param name="resolvers">All registered content resolvers.</param>
/// <param name="deliverers">All registered content deliverers.</param>
/// <param name="logger">Logger instance.</param>
public class ContentPipelineFactory(
    IEnumerable<IContentDiscoverer> discoverers,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    ILogger<ContentPipelineFactory> logger) : IContentPipelineFactory
{
    private readonly IReadOnlyList<IContentDiscoverer> _discoverers = discoverers.ToList();
    private readonly IReadOnlyList<IContentResolver> _resolvers = resolvers.ToList();
    private readonly IReadOnlyList<IContentDeliverer> _deliverers = deliverers.ToList();

    /// <inheritdoc/>
    public IContentDiscoverer? GetDiscoverer(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        var normalized = providerId.Replace("-", string.Empty);
        var discoverer = _discoverers.FirstOrDefault(d =>
            d.SourceName.Equals(providerId, StringComparison.OrdinalIgnoreCase) ||
            d.SourceName.Equals(normalized, StringComparison.OrdinalIgnoreCase));

        if (discoverer == null)
        {
            logger.LogDebug("No discoverer found for provider ID '{ProviderId}'", providerId);
        }

        return discoverer;
    }

    /// <inheritdoc/>
    public IContentResolver? GetResolver(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        var normalized = providerId.Replace("-", string.Empty);
        var resolver = _resolvers.FirstOrDefault(r =>
            r.ResolverId.Equals(providerId, StringComparison.OrdinalIgnoreCase) ||
            r.ResolverId.Equals(normalized, StringComparison.OrdinalIgnoreCase));

        if (resolver == null)
        {
            logger.LogDebug("No resolver found for provider ID '{ProviderId}'", providerId);
        }

        return resolver;
    }

    /// <inheritdoc/>
    public IContentDeliverer? GetDeliverer(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        var normalized = providerId.Replace("-", string.Empty);
        var deliverer = _deliverers.FirstOrDefault(d =>
            d.SourceName.Equals(providerId, StringComparison.OrdinalIgnoreCase) ||
            d.SourceName.Equals(normalized, StringComparison.OrdinalIgnoreCase));

        if (deliverer == null)
        {
            logger.LogDebug("No deliverer found for provider ID '{ProviderId}'", providerId);
        }

        return deliverer;
    }

    /// <inheritdoc/>
    public IEnumerable<IContentDiscoverer> GetAllDiscoverers() => _discoverers;

    /// <inheritdoc/>
    public IEnumerable<IContentResolver> GetAllResolvers() => _resolvers;

    /// <inheritdoc/>
    public IEnumerable<IContentDeliverer> GetAllDeliverers() => _deliverers;

    /// <inheritdoc/>
    public (IContentDiscoverer? Discoverer, IContentResolver? Resolver, IContentDeliverer? Deliverer)
        GetPipeline(ProviderDefinition provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var providerId = provider.ProviderId;

        logger.LogDebug("Getting pipeline for provider '{ProviderId}'", providerId);

        var discoverer = GetDiscoverer(providerId);
        var resolver = GetResolver(providerId);
        var deliverer = GetDeliverer(providerId);

        logger.LogDebug(
            "Pipeline for '{ProviderId}': Discoverer={HasDiscoverer}, Resolver={HasResolver}, Deliverer={HasDeliverer}",
            providerId,
            discoverer != null,
            resolver != null,
            deliverer != null);

        return (discoverer, resolver, deliverer);
    }
}
