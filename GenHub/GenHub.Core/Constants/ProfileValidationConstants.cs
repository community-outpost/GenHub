namespace GenHub.Core.Constants;

/// <summary>
/// Constants for game profile validation messages and rules.
/// </summary>
public static class ProfileValidationConstants
{
    /// <summary>
    /// Error message when a game installation is required but missing.
    /// </summary>
    public const string MissingGameInstallation = "At least one game installation content item must be enabled for launch";

    /// <summary>
    /// Error message when a game client is required but missing.
    /// </summary>
    public const string MissingGameClient = "At least one game client content item must be enabled for launch";

    /// <summary>
    /// Error message when a Tool profile has no ToolContentId set.
    /// </summary>
    public const string ToolProfileMissingContentId = "Tool profile must have ToolContentId set";

    /// <summary>
    /// Error message when attempting to mix Tool content with other content types.
    /// </summary>
    public const string ToolProfileMixedContentNotAllowed = "Tool profiles can only contain exactly one ModdingTool content item";

    /// <summary>
    /// Error message for Tool profile with multiple ModdingTool items.
    /// </summary>
    public const string ToolProfileMultipleToolsNotAllowed = "Tool profiles can only contain one ModdingTool content item";

    /// <summary>
    /// The exact number of ModdingTool items required for a Tool profile.
    /// </summary>
    public const int ToolProfileRequiredModdingToolCount = 1;

    /// <summary>
    /// The maximum total content items allowed for a Tool profile.
    /// </summary>
    public const int ToolProfileMaxContentItems = 1;

    /// <summary>
    /// Message shown when settings are accessed for a Tool profile.
    /// </summary>
    public const string ToolProfileSettingsNotApplicable = "Settings are not applicable for Tool profiles";

    /// <summary>
    /// Message shown when invalid parameters are passed to tool profile validation.
    /// </summary>
    public const string InvalidToolProfileParameters = "Invalid parameters for Tool Profile validation";
}
