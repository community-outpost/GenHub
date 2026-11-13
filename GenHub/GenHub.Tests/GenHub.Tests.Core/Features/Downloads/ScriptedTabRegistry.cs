using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Results.Content;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Downloads;

/// <summary>
/// A tab provider registry whose responses the test completes per catalog content ID.
/// </summary>
internal sealed class ScriptedTabRegistry : ITabProviderRegistry
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IReadOnlyList<CustomTabDefinition>>> _responses = new();
    private readonly List<string> _requested = [];
    private readonly List<CancellationToken> _tokens = [];

    /// <summary>Gets the content IDs requested so far, in order.</summary>
    public IReadOnlyList<string> RequestedContentIds
    {
        get
        {
            lock (_requested)
            {
                return [.. _requested];
            }
        }
    }

    /// <summary>Gets the cancellation tokens passed with each request, in order.</summary>
    public IReadOnlyList<CancellationToken> RequestTokens
    {
        get
        {
            lock (_requested)
            {
                return [.. _tokens];
            }
        }
    }

    /// <summary>Makes the next request for a content ID wait on a new pending response.</summary>
    /// <param name="contentId">The catalog content ID.</param>
    /// <returns>The response source to complete.</returns>
    public TaskCompletionSource<IReadOnlyList<CustomTabDefinition>> Pending(string contentId)
    {
        var source = new TaskCompletionSource<IReadOnlyList<CustomTabDefinition>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _responses[contentId] = source;
        return source;
    }

    /// <summary>Makes requests for a content ID return the given tabs.</summary>
    /// <param name="contentId">The catalog content ID.</param>
    /// <param name="tabs">The tabs to return.</param>
    public void Complete(string contentId, params CustomTabDefinition[] tabs) => Pending(contentId).SetResult(tabs);

    /// <inheritdoc />
    public void RegisterProvider(ITabProvider provider) => throw new NotSupportedException();

    /// <inheritdoc />
    public bool UnregisterProvider(string providerId) => throw new NotSupportedException();

    /// <inheritdoc />
    public IReadOnlyList<ITabProvider> GetAllProviders() => [];

    /// <inheritdoc />
    public Task<IReadOnlyList<CustomTabDefinition>> GetTabsForContentAsync(
        ContentSearchResult searchResult,
        CancellationToken cancellationToken = default)
    {
        var contentId = searchResult.ResolverMetadata[CatalogConstants.CatalogContentIdMetadataKey];
        lock (_requested)
        {
            _requested.Add(contentId);
            _tokens.Add(cancellationToken);
        }

        return _responses[contentId].Task;
    }
}
