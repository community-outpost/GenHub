using System.Collections.Generic;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;

namespace GenHub.Features.Content.Services.CommunityOutpost.Models;

/// <summary>
/// Represents metadata for a GenPatcher content code, providing mappings to GenHub content types.
/// </summary>
public class GenPatcherContentMetadata
{
    /// <summary>
    /// Gets or sets the 4-character content code.
    /// </summary>
    public string ContentCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name for this content.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description for this content.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content type.
    /// </summary>
    public ContentType ContentType { get; set; } = ContentType.UnknownContentType;

    /// <summary>
    /// Gets or sets the target game.
    /// </summary>
    public GameType TargetGame { get; set; } = GameType.Unknown;

    /// <summary>
    /// Gets or sets the language code (e.g., "en", "de", "fr") if applicable.
    /// </summary>
    public string? LanguageCode { get; set; }

    /// <summary>
    /// Gets or sets the version string derived from the content code.
    /// </summary>
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Gets or sets the content category for grouping in UI.
    /// </summary>
    public GenPatcherContentCategory Category { get; set; } = GenPatcherContentCategory.Other;

    /// <summary>
    /// Gets or sets a value indicating whether this content is exclusive
    /// (only one of its category can be active at a time).
    /// </summary>
    public bool IsExclusive { get; set; } = false;

    /// <summary>
    /// Gets or sets the list of content codes that conflict with this content.
    /// </summary>
    public List<string> ConflictsWith { get; set; } = new();

    /// <summary>
    /// Gets or sets the target installation location for this content.
    /// Maps and missions go to UserMapsDirectory, most other content goes to Workspace.
    /// </summary>
    public ContentInstallTarget InstallTarget { get; set; } = ContentInstallTarget.Workspace;

    /// <summary>
    /// Gets the dependencies for this content.
    /// </summary>
    /// <returns>List of content dependencies.</returns>
    public List<ContentDependency> GetDependencies()
    {
        return GenPatcherDependencyBuilder.GetDependencies(ContentCode, this);
    }

    /// <summary>
    /// Gets a value indicating whether this content has dependencies.
    /// </summary>
    public bool HasDependencies => Category != GenPatcherContentCategory.BaseGame &&
                                   Category != GenPatcherContentCategory.Prerequisites;
}
