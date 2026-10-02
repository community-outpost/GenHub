using System.Text.Json.Serialization;

namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// JSON models for Steam Web API published-file responses (IPublishedFileService/QueryFiles,
/// ISteamRemoteStorage/GetPublishedFileDetails).
/// </summary>
public sealed record SteamWorkshopQueryResponse(
    [property: JsonPropertyName("response")] SteamWorkshopQueryBody? Response);
