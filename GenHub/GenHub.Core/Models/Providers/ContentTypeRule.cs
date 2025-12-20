using System.Text.Json.Serialization;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Rule for mapping discovered content to content types.
/// </summary>
public class ContentTypeRule
{
    /// <summary>
    /// Gets or sets the pattern to match (regex or simple match).
    /// </summary>
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the field to match against (name, description, tags).
    /// </summary>
    [JsonPropertyName("matchField")]
    public string MatchField { get; set; } = "name";

    /// <summary>
    /// Gets or sets the resulting content type.
    /// </summary>
    [JsonPropertyName("contentType")]
    public ContentType ContentType { get; set; } = ContentType.Mod;
}
