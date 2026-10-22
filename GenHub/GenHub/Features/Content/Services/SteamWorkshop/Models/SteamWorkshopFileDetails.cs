using System;
using System.Collections.Generic;

namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// Represents structured data parsed from a workshop file details page.
/// </summary>
/// <param name="Title">The item title.</param>
/// <param name="DescriptionHtml">The raw description HTML.</param>
/// <param name="Author">The creator display name.</param>
/// <param name="PreviewUrl">The main preview image URL.</param>
/// <param name="FileSizeText">The raw file size text (e.g. "670.551 KB").</param>
/// <param name="Posted">The posted date.</param>
/// <param name="Updated">The updated date.</param>
/// <param name="Tags">The workshop tags.</param>
/// <param name="AppId">The workshop application ID, when the page reports one.</param>
/// <param name="Screenshots">Additional gallery image URLs beyond the main preview.</param>
/// <param name="CreatorAvatarUrl">The creator avatar image URL, when present.</param>
/// <param name="Stats">The raw details-page stat label/value pairs.</param>
public sealed record SteamWorkshopFileDetails(
    string? Title,
    string? DescriptionHtml,
    string? Author,
    string? PreviewUrl,
    string? FileSizeText,
    DateTime? Posted,
    DateTime? Updated,
    IReadOnlyList<string> Tags,
    int? AppId = null,
    IReadOnlyList<string>? Screenshots = null,
    string? CreatorAvatarUrl = null,
    IReadOnlyDictionary<string, string>? Stats = null);
