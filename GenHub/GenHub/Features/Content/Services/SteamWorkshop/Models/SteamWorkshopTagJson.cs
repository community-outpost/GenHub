using System.Text.Json.Serialization;

namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// Represents a workshop tag in Web API responses.
/// </summary>
/// <param name="Tag">The tag value.</param>
public sealed record SteamWorkshopTagJson(
    [property: JsonPropertyName("tag")] string? Tag);
