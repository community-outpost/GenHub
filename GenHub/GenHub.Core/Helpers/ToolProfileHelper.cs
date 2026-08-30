using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;

namespace GenHub.Core.Helpers;

/// <summary>
/// Centralized helper for Tool Profile detection and validation logic.
/// </summary>
public static class ToolProfileHelper
{
    /// <summary>
    /// Determines if a profile should be treated as a Tool Profile based on its enabled content.
    /// A Tool Profile has exactly one ModdingTool content item and no other content types.
    /// </summary>
    /// <param name="enabledContentIds">The list of enabled content IDs.</param>
    /// <param name="manifestPool">The manifest pool to look up content types.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if this should be a Tool Profile, false otherwise.</returns>
    public static async Task<bool> IsToolProfileAsync(
        IEnumerable<string> enabledContentIds,
        IContentManifestPool manifestPool,
        CancellationToken cancellationToken = default)
    {
        if (manifestPool == null || enabledContentIds == null)
        {
            return false;
        }

        var contentIdsList = enabledContentIds.ToList();

        // Tool profiles must have exactly one content item
        if (contentIdsList.Count != ProfileValidationConstants.ToolProfileMaxContentItems)
        {
            return false;
        }

        // Check if that one item is a ModdingTool
        var singleContentId = contentIdsList.First();
        var manifestResult = await manifestPool.GetManifestAsync(singleContentId, cancellationToken);

        if (manifestResult.Failed || manifestResult.Data == null)
        {
            return false;
        }

        return manifestResult.Data.ContentType.IsStandalone();
    }

    /// <summary>
    /// Validates that a Tool Profile has the correct content configuration.
    /// Returns null if valid, or an error message if invalid.
    /// </summary>
    /// <param name="enabledContentIds">The list of enabled content IDs.</param>
    /// <param name="manifestPool">The manifest pool to look up content types.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Error message if invalid, null if valid.</returns>
    public static async Task<string?> ValidateToolProfileContentAsync(
        IEnumerable<string> enabledContentIds,
        IContentManifestPool manifestPool,
        CancellationToken cancellationToken = default)
    {
        if (manifestPool == null || enabledContentIds == null)
        {
            return ProfileValidationConstants.InvalidToolProfileParameters;
        }

        var contentIdsList = enabledContentIds.ToList();

        // Load all manifests
        var moddingToolCount = 0;
        var otherContentCount = 0;

        foreach (var contentId in contentIdsList)
        {
            var manifestResult = await manifestPool.GetManifestAsync(contentId, cancellationToken);
            if (manifestResult.Success && manifestResult.Data != null)
            {
                if (manifestResult.Data.ContentType.IsStandalone())
                {
                    moddingToolCount++;
                }
                else
                {
                    otherContentCount++;
                }
            }
        }

        // Tool profiles must have exactly one ModdingTool
        if (moddingToolCount != ProfileValidationConstants.ToolProfileRequiredModdingToolCount)
        {
            return moddingToolCount > 1
                ? ProfileValidationConstants.ToolProfileMultipleToolsNotAllowed
                : ProfileValidationConstants.ToolProfileMixedContentNotAllowed;
        }

        // Tool profiles cannot have any other content types
        if (otherContentCount > 0)
        {
            return ProfileValidationConstants.ToolProfileMixedContentNotAllowed;
        }

        return null; // Valid
    }

    /// <summary>
    /// Synchronous version for UI layer where manifests are already loaded.
    /// Determines if the enabled content represents a Tool Profile.
    /// </summary>
    /// <param name="enabledContent">Collection of content items with their types.</param>
    /// <returns>True if this is a Tool Profile configuration.</returns>
    public static bool IsToolProfile(IEnumerable<(string ManifestId, ContentType ContentType)> enabledContent)
    {
        var contentList = enabledContent.ToList();

        // Must have exactly one content item
        if (contentList.Count != ProfileValidationConstants.ToolProfileMaxContentItems)
        {
            return false;
        }

        // That one item must be a standalone tool (ModdingTool, Executable, Addon)
        return contentList[0].ContentType.IsStandalone();
    }

    /// <summary>
    /// Synchronous validation for UI layer where manifests are already loaded.
    /// Returns error message if invalid, null if valid.
    /// </summary>
    /// <param name="enabledContent">Collection of content items with their types.</param>
    /// <returns>Error message if invalid, null if valid.</returns>
    public static string? ValidateToolProfileContent(IEnumerable<(string ManifestId, ContentType ContentType)> enabledContent)
    {
        var contentList = enabledContent.ToList();

        var moddingToolCount = contentList.Count(c => c.ContentType.IsStandalone());
        var otherContentCount = contentList.Count - moddingToolCount;

        // Tool profiles must have exactly one ModdingTool
        if (moddingToolCount != ProfileValidationConstants.ToolProfileRequiredModdingToolCount)
        {
            return moddingToolCount > 1
                ? ProfileValidationConstants.ToolProfileMultipleToolsNotAllowed
                : ProfileValidationConstants.ToolProfileMixedContentNotAllowed;
        }

        // Tool profiles cannot have any other content types
        if (otherContentCount > 0)
        {
            return ProfileValidationConstants.ToolProfileMixedContentNotAllowed;
        }

        return null; // Valid
    }
}
