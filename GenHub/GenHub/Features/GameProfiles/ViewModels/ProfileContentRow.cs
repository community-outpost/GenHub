namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// One row on the Game or Content page of the profile settings window.
/// </summary>
/// <param name="Item">The content the row represents.</param>
/// <param name="IsIncluded">Whether the content is part of the profile.</param>
public sealed record ProfileContentRow(ContentDisplayItem Item, bool IsIncluded);
