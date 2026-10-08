using System.Text.Json;

namespace GenHub.Core.Helpers;

/// <summary>
/// Shared hardened JSON options for content manifest deserialization.
/// </summary>
public static class ManifestJsonOptions
{
    /// <summary>
    /// Gets the default hardened options for manifest payloads.
    /// </summary>
    public static JsonSerializerOptions Default { get; } = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 32,
    };
}
