using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// Represents a single published file in Web API responses.
/// </summary>
public sealed record SteamWorkshopFileJson(
    [property: JsonPropertyName("publishedfileid")] string? PublishedFileId,
    [property: JsonPropertyName("result")] int Result,
    [property: JsonPropertyName("creator")] string? Creator,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("file_description")] string? FileDescription,
    [property: JsonPropertyName("short_description")] string? ShortDescription,
    [property: JsonPropertyName("time_created")] long TimeCreated,
    [property: JsonPropertyName("time_updated")] long TimeUpdated,
    [property: JsonPropertyName("subscriptions")] int Subscriptions,
    [property: JsonPropertyName("favorited")] int Favorited,
    [property: JsonPropertyName("views")] int Views,
    [property: JsonPropertyName("file_size")]
    [property: JsonConverter(typeof(SteamWorkshopFileSizeConverter))] long FileSize,
    [property: JsonPropertyName("preview_url")] string? PreviewUrl,
    [property: JsonPropertyName("tags")] List<SteamWorkshopTagJson>? Tags,
    [property: JsonPropertyName("previews")] List<SteamWorkshopPreviewJson>? Previews,
    [property: JsonPropertyName("consumer_app_id")] int ConsumerAppId = 0,
    [property: JsonPropertyName("creator_app_id")] int CreatorAppId = 0,
    [property: JsonPropertyName("hcontent_file")] string? HContentFile = null,
    [property: JsonPropertyName("file_url")] string? FileUrl = null);
