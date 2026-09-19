using System.Text.Json;

namespace GenHub.Core.Helpers;

/// <summary>
/// Shared JSON options for publisher definition and catalog documents.
/// </summary>
public static class PublisherJsonOptions
{
    /// <summary>
    /// Gets lenient, case-insensitive options for publisher definition and catalog documents.
    /// </summary>
    public static JsonSerializerOptions Definition { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
