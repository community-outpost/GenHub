using System.Text.Json.Serialization;

namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// Represents a preview entry in Web API responses.
/// </summary>
/// <param name="Url">The preview URL.</param>
public sealed record SteamWorkshopPreviewJson(
    [property: JsonPropertyName("url")] string? Url);
