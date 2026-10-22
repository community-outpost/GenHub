namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// Represents a single item parsed from a workshop browse page.
/// </summary>
/// <param name="PublishedFileId">The workshop published file ID.</param>
/// <param name="Title">The item title.</param>
/// <param name="Author">The creator display name, when present.</param>
/// <param name="PreviewUrl">The preview image URL, when present.</param>
/// <param name="DetailsUrl">The absolute file details page URL.</param>
/// <param name="AuthorProfileUrl">The creator's profile page URL, when present.</param>
/// <param name="CreatorId">The creator's Steam ID, when resolvable from the browse page.</param>
/// <param name="CreatorAvatarUrl">The creator's avatar image URL, when present in embedded page data.</param>
public sealed record SteamWorkshopBrowseItem(
    string PublishedFileId,
    string Title,
    string? Author,
    string? PreviewUrl,
    string DetailsUrl,
    string? AuthorProfileUrl = null,
    string? CreatorId = null,
    string? CreatorAvatarUrl = null);
