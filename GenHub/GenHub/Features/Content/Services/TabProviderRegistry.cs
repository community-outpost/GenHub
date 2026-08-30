using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Registry for managing custom tab providers.
/// </summary>
public class TabProviderRegistry : ITabProviderRegistry
{
    private readonly ConcurrentDictionary<string, ITabProvider> _providers = new();
    private readonly ILogger<TabProviderRegistry> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TabProviderRegistry"/> class and registers every tab provider known to dependency injection.
    /// </summary>
    /// <param name="providers">The registered tab providers.</param>
    /// <param name="logger">The logger instance.</param>
    public TabProviderRegistry(
        IEnumerable<ITabProvider> providers,
        ILogger<TabProviderRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        foreach (var provider in providers)
        {
            RegisterProvider(provider);
        }
    }

    /// <inheritdoc/>
    public void RegisterProvider(ITabProvider provider)
    {
        if (_providers.TryAdd(provider.ProviderId, provider))
        {
            _logger.LogInformation("registered tab provider: {ProviderId}", provider.ProviderId);
        }
        else
        {
            _logger.LogWarning("tab provider already registered: {ProviderId}", provider.ProviderId);
        }
    }

    /// <inheritdoc/>
    public bool UnregisterProvider(string providerId)
    {
        if (_providers.TryRemove(providerId, out _))
        {
            _logger.LogInformation("unregistered tab provider: {ProviderId}", providerId);
            return true;
        }

        _logger.LogWarning("tab provider not found: {ProviderId}", providerId);
        return false;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ITabProvider> GetAllProviders()
    {
        return [.. _providers.Values];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CustomTabDefinition>> GetTabsForContentAsync(
        ContentSearchResult searchResult,
        CancellationToken cancellationToken = default)
    {
        var allTabs = new List<CustomTabDefinition>();

        foreach (var provider in _providers.Values)
        {
            try
            {
                if (provider.CanProvideTabsFor(searchResult))
                {
                    var tabs = await provider.GetTabsAsync(searchResult, cancellationToken);
                    allTabs.AddRange(tabs);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "error getting tabs from provider {ProviderId}", provider.ProviderId);
            }
        }

        // Sort by order and return read-only list
        return [.. allTabs.OrderBy(t => t.Order)];
    }
}
