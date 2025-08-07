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
    private readonly ConcurrentDictionary<string, GameManifest> _manifests = new();
    private readonly ConcurrentDictionary<string, bool> _casObjectExists = new();

    /// <inheritdoc />
    public GameManifest? GetManifest(string manifestId)
    {
        return _manifests.TryGetValue(manifestId, out var manifest) ? manifest : null;
    }

    /// <inheritdoc />
    public void AddOrUpdateManifest(GameManifest manifest)
    {
        _manifests[manifest.Id] = manifest;
    }

    /// <inheritdoc />
    public IEnumerable<GameManifest> GetAllManifests()
    {
        return _manifests.Values;
    }

    /// <summary>
    /// Gets whether a CAS object exists for the specified hash.
    /// </summary>
    /// <param name="hash">The hash of the CAS object.</param>
    /// <returns>
    /// True if the CAS object exists, false if it does not exist, or null if unknown.
    /// </returns>
    public bool? GetCasObjectExists(string hash)
    {
        return _casObjectExists.TryGetValue(hash, out var exists) ? exists : null;
    }

    /// <summary>
    /// Sets whether a CAS object exists for the specified hash.
    /// This is used to simulate CAS object existence in tests.
    /// </summary>
    /// <param name="hash">The hash of the CAS object.</param>
    /// <param name="exists">True if the CAS object exists; otherwise, false.</param>
    public void SetCasObjectExists(string hash, bool exists)
    {
        _casObjectExists[hash] = exists;
    }
}
