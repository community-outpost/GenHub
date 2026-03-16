using System.Collections.Concurrent;
using GenHub.Core.Interfaces.Common;

namespace GenHub.Common.Services;

/// <summary>
/// Implementation of <see cref="ISessionPreferenceService"/> using an in-memory dictionary.
/// </summary>
public class SessionPreferenceService : ISessionPreferenceService
{
    private readonly ConcurrentDictionary<string, bool> _skipConfirmations = new();

    /// <inheritdoc/>
    public bool ShouldSkipConfirmation(string key)
    {
        return _skipConfirmations.TryGetValue(key, out var skip) && skip;
    }

    /// <inheritdoc/>
    public void SetSkipConfirmation(string key, bool skip)
    {
        _skipConfirmations[key] = skip;
    }
}
