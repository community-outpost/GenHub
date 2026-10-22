using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Community message of the day from the MOTD endpoint.
/// </summary>
public sealed class GeneralsOnlineMotdResult
{
    /// <summary>
    /// Gets or sets the message text, already formatted with live counts.
    /// </summary>
    [JsonPropertyName("MOTD")]
    public string Motd { get; set; } = string.Empty;
}
