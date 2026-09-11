using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Extensions;

/// <summary>
/// Extension methods for dictionary collections.
/// </summary>
public static class DictionaryExtensions
{
    /// <summary>
    /// Attempts to retrieve a value from the dictionary by key using case-insensitive matching.
    /// </summary>
    /// <param name="dict">The dictionary to search.</param>
    /// <param name="key">The key to locate.</param>
    /// <param name="value">When this method returns, contains the value associated with the specified key, or empty string if not found.</param>
    /// <returns><c>true</c> if the key was found; otherwise, <c>false</c>.</returns>
    public static bool TryGetCaseInsensitive(this IReadOnlyDictionary<string, string> dict, string key, out string value)
    {
        if (dict.TryGetValue(key, out var val))
        {
            value = val;
            return true;
        }

        var match = dict.FirstOrDefault(kvp => string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase));
        if (match.Key != null)
        {
            value = match.Value;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
