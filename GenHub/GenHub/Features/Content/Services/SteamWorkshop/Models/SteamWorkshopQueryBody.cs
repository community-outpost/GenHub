using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// Represents the body of a QueryFiles response.
/// </summary>
/// <param name="Total">The total match count.</param>
/// <param name="PublishedFileDetails">The returned file details.</param>
public sealed record SteamWorkshopQueryBody(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("publishedfiledetails")] List<SteamWorkshopFileJson>? PublishedFileDetails);
