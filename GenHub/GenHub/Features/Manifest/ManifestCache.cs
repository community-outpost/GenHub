using System.Collections.Concurrent;
using System.Collections.Generic;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Manifest;

namespace GenHub.Features.Manifest;

/// <summary>
/// A thread-safe, in-memory cache for storing and retrieving game manifests.
/// </summary>
public class ManifestCache() : IManifestCache
{
    private readonly ConcurrentDictionary<string, ContentManifest> _manifests = new();

    /// <inheritdoc />
    public ContentManifest? GetManifest(string manifestId)
    {
        return _manifests.TryGetValue(manifestId, out var manifest) ? manifest : null;
    }

    /// <inheritdoc />
    public void AddOrUpdateManifest(ContentManifest manifest)
    {
        _manifests[manifest.Id] = manifest;
    }

    /// <inheritdoc />
    public IEnumerable<ContentManifest> GetAllManifests()
    {
        return _manifests.Values;
    }
}
