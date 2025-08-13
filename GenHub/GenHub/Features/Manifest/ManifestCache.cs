using System.Collections.Concurrent;
using System.Collections.Generic;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Manifest;

namespace GenHub.Features.Manifest;

/// <summary>
/// A thread-safe, in-memory cache for storing and retrieving game manifests and CAS object metadata.
/// </summary>
public class ManifestCache() : IManifestCache
{
    private readonly ConcurrentDictionary<string, ContentManifest> _manifests = new();
    private readonly ConcurrentDictionary<string, bool> _casObjectExists = new();

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

    /// <summary>
    /// Checks if a CAS object is known to exist in the cache.
    /// </summary>
    /// <param name="hash">The hash of the CAS object.</param>
    /// <returns>True if the object is known to exist, false otherwise.</returns>
    public bool CasObjectExists(string hash)
    {
        return _casObjectExists.ContainsKey(hash);
    }

    /// <summary>
    /// Marks a CAS object as existing in the cache.
    /// </summary>
    /// <param name="hash">The hash of the CAS object.</param>
    public void MarkCasObjectAsExisting(string hash)
    {
        _casObjectExists.TryAdd(hash, true);
    }
}
