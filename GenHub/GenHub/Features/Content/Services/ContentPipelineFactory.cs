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
public class ContentPipelineFactory : IContentPipelineFactory
{
    private readonly IEnumerable<IContentDiscoverer> _discoverers;
    private readonly IEnumerable<IContentResolver> _resolvers;
    private readonly IEnumerable<IContentDeliverer> _deliverers;
    private readonly ILogger<ContentPipelineFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentPipelineFactory"/> class.
    /// </summary>
    /// <param name="discoverers">All registered content discoverers.</param>
    /// <param name="resolvers">All registered content resolvers.</param>
    /// <param name="deliverers">All registered content deliverers.</param>
    /// <param name="logger">Logger instance.</param>
    public ContentPipelineFactory(
        IEnumerable<IContentDiscoverer> discoverers,
        IEnumerable<IContentResolver> resolvers,
        IEnumerable<IContentDeliverer> deliverers,
        ILogger<ContentPipelineFactory> logger)
    {
        _discoverers = discoverers;
        _resolvers = resolvers;
        _deliverers = deliverers;
        _logger = logger;

        _logger.LogDebug(
            "ContentPipelineFactory initialized with {DiscovererCount} discoverers, {ResolverCount} resolvers, {DelivererCount} deliverers",
            _discoverers.Count(),
            _resolvers.Count(),
            _deliverers.Count());
    }

    /// <inheritdoc/>
    public IContentDiscoverer? GetDiscoverer(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        // Match by SourceName (case-insensitive)
        var discoverer = _discoverers.FirstOrDefault(d =>
            d.SourceName.Equals(providerId, StringComparison.OrdinalIgnoreCase));

        if (discoverer == null)
        {
            _logger.LogDebug("No discoverer found for provider ID '{ProviderId}'", providerId);
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

        // Match by ResolverId (case-insensitive)
        var resolver = _resolvers.FirstOrDefault(r =>
            r.ResolverId.Equals(providerId, StringComparison.OrdinalIgnoreCase));

        if (resolver == null)
        {
            _logger.LogDebug("No resolver found for provider ID '{ProviderId}'", providerId);
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

        // Match by SourceName (case-insensitive)
        var deliverer = _deliverers.FirstOrDefault(d =>
            d.SourceName.Equals(providerId, StringComparison.OrdinalIgnoreCase));

        if (deliverer == null)
        {
            _logger.LogDebug("No deliverer found for provider ID '{ProviderId}'", providerId);
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

        _logger.LogDebug("Getting pipeline for provider '{ProviderId}'", providerId);

        var discoverer = GetDiscoverer(providerId);
        var resolver = GetResolver(providerId);
        var deliverer = GetDeliverer(providerId);

        _logger.LogDebug(
            "Pipeline for '{ProviderId}': Discoverer={HasDiscoverer}, Resolver={HasResolver}, Deliverer={HasDeliverer}",
            providerId,
            discoverer != null,
            resolver != null,
            deliverer != null);

        return (discoverer, resolver, deliverer);
    }
}
