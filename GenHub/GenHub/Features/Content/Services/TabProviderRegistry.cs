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
/// <param name="providers">The registered tab providers.</param>
/// <param name="logger">The logger instance.</param>
public class TabProviderRegistry(
    IEnumerable<ITabProvider> providers,
    ILogger<TabProviderRegistry> logger) : ITabProviderRegistry
{
    private readonly ConcurrentDictionary<string, ITabProvider> _providers = InitializeProviders(providers, logger);

    /// <inheritdoc/>
    public void RegisterProvider(ITabProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (_providers.TryAdd(provider.ProviderId, provider))
        {
            logger.LogInformation("registered tab provider: {ProviderId}", provider.ProviderId);
        }
        else
        {
            logger.LogWarning("tab provider already registered: {ProviderId}", provider.ProviderId);
        }
    }

    /// <inheritdoc/>
    public bool UnregisterProvider(string providerId)
    {
        if (_providers.TryRemove(providerId, out _))
        {
            logger.LogInformation("unregistered tab provider: {ProviderId}", providerId);
            return true;
        }

        logger.LogWarning("tab provider not found: {ProviderId}", providerId);
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "error getting tabs from provider {ProviderId}", provider.ProviderId);
            }
        }

        // Sort by order and return read-only list
        return [.. allTabs.OrderBy(t => t.Order)];
    }

    private static ConcurrentDictionary<string, ITabProvider> InitializeProviders(
        IEnumerable<ITabProvider> providers,
        ILogger<TabProviderRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(logger);

        var dict = new ConcurrentDictionary<string, ITabProvider>();
        foreach (var provider in providers)
        {
            if (dict.TryAdd(provider.ProviderId, provider))
            {
                logger.LogInformation("registered tab provider: {ProviderId}", provider.ProviderId);
            }
            else
            {
                logger.LogWarning("tab provider already registered: {ProviderId}", provider.ProviderId);
            }
        }

        return dict;
    }
}
