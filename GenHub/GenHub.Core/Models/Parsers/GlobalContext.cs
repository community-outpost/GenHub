namespace GenHub.Core.Models.Parsers;

/// <summary>
/// Represents the global context information extracted from a web page header.
/// Typically parsed from elements like .headerbox that contain the parent entity information.
/// </summary>
/// <param name="Title">The title of the content (mod, addon, etc.).</param>
/// <param name="Developer">The developer/publisher name.</param>
/// <param name="ReleaseDate">The release date of the content.</param>
/// <param name="GameName">The name of the game this content is for (optional).</param>
/// <param name="IconUrl">URL to the main icon/preview image (optional).</param>
/// <param name="Description">Brief description or summary (optional).</param>
public record GlobalContext(
    string Title,
    string Developer,
    DateTime? ReleaseDate,
    string? GameName = null,
    string? IconUrl = null,
    string? Description = null);
