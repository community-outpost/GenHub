using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Single lobby payload of the backend GET Lobby endpoint.
/// </summary>
public sealed class GeneralsOnlineLobbyResult
{
    /// <summary>
    /// Gets or sets the lobby, or null when it no longer exists.
    /// </summary>
    [JsonPropertyName("lobby")]
    public GeneralsOnlineLobby? Lobby { get; set; }
}
