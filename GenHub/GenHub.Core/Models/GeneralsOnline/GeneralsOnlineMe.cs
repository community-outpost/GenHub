using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Result payload of the backend GET Users/Me endpoint.
/// </summary>
public sealed class GeneralsOnlineMe
{
    /// <summary>
    /// Gets or sets the signed-in user id.
    /// </summary>
    [JsonPropertyName("user_id")]
    public long UserId { get; set; } = -1;

    /// <summary>
    /// Gets or sets the signed-in display name.
    /// </summary>
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;
}
