using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// An active user entry from the Users/Active endpoint.
/// </summary>
public sealed class GeneralsOnlineActiveUser
{
    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the status text.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets the session duration text.
    /// </summary>
    [JsonPropertyName("duration")]
    public string? Duration { get; set; }
}
