using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>
    /// Gets lenient, case-insensitive options with enum conversion for publisher catalog imports.
    /// </summary>
    public static JsonSerializerOptions CatalogImport { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
