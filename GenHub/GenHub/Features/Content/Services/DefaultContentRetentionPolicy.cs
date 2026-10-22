using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Default retention policy: retains manifests referenced by pinned providers (e.g. replays)
/// as well as the N most recent versions of content.
/// </summary>
public class DefaultContentRetentionPolicy(
    ILogger<DefaultContentRetentionPolicy> logger,
    IEnumerable<IPinnedManifestProvider>? pinnedProviders = null,
    int recentVersionsToRetain = DefaultContentRetentionPolicy.DefaultRecentVersionsToRetain) : IContentRetentionPolicy
{
    /// <summary>
    /// Default number of most recent versions to retain per content group.
    /// </summary>
    public const int DefaultRecentVersionsToRetain = 2;

    private readonly ILogger<DefaultContentRetentionPolicy> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IEnumerable<IPinnedManifestProvider> _pinnedProviders = pinnedProviders ?? [];
    private readonly int _recentVersionsToRetain = recentVersionsToRetain >= 0 ? recentVersionsToRetain : DefaultRecentVersionsToRetain;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContentManifest>> FilterDeletableManifestsAsync(
        IEnumerable<ContentManifest> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var candidateList = candidates.ToList();
        if (candidateList.Count == 0)
        {
            return [];
        }

        var pinnedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in _pinnedProviders)
        {
            try
            {
                var ids = await provider.GetPinnedManifestIdsAsync(cancellationToken);
                if (ids != null)
                {
                    pinnedIds.UnionWith(ids);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to retrieve pinned manifest IDs from provider {Provider}", provider.GetType().Name);
            }
        }

        var deletable = new List<ContentManifest>();
        var grouped = candidateList.GroupBy(m => $"{m.Publisher?.PublisherType}:{m.ContentType}:{m.TargetGame}");

        foreach (var group in grouped)
        {
            var unpinned = group.Where(m => !pinnedIds.Contains(m.Id.Value)).ToList();

            if (unpinned.Count <= _recentVersionsToRetain)
            {
                _logger.LogDebug(
                    "Retaining all {Count} manifests for group {Group} within recent quota ({Quota})",
                    unpinned.Count,
                    group.Key,
                    _recentVersionsToRetain);
                continue;
            }

            var sorted = unpinned
                .OrderByDescending(m => m.Metadata?.ReleaseDate ?? DateTime.MinValue)
                .ThenByDescending(m => m.Version, Comparer<string?>.Create(CatalogManifestIdentity.CompareVersions))
                .ThenBy(m => m.Id.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var toRetain = sorted.Take(_recentVersionsToRetain).ToList();
            var toDelete = sorted.Skip(_recentVersionsToRetain).ToList();

            _logger.LogInformation(
                "Retention policy for {Group}: retaining {RetainCount} recent, marking {DeleteCount} for removal (pinned: {PinnedCount})",
                group.Key,
                toRetain.Count,
                toDelete.Count,
                group.Count() - unpinned.Count);

            deletable.AddRange(toDelete);
        }

        return deletable;
    }
}
