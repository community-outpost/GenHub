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
    /// Error message when a Tool profile has an invalid ToolContentId.
    /// </summary>
    public const string InvalidToolContentId = "Tool profile has an invalid ToolContentId";

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

    /// <summary>
    /// Error message when tool manifest fails to load.
    /// </summary>
    public const string FailedToLoadToolManifest = "Failed to load tool manifest";

    /// <summary>
    /// Error message when tool workspace preparation fails.
    /// </summary>
    public const string FailedToPrepareToolWorkspace = "Failed to prepare tool workspace";

    /// <summary>
    /// Error message when tool manifest is missing an executable.
    /// </summary>
    public const string ToolManifestMissingExecutable = "Tool manifest does not contain an executable file";

    /// <summary>
    /// Error message when tool executable is not found on disk.
    /// </summary>
    public const string ToolExecutableNotFound = "Tool executable not found";

    /// <summary>
    /// Error message when tool process fails to start.
    /// </summary>
    public const string ToolProcessStartFailed = "Failed to start tool process (Process.Start returned null)";

    /// <summary>
    /// Notification title when tool launches successfully.
    /// </summary>
    public const string ToolLaunchSuccessTitle = "Tool Launched";

    /// <summary>
    /// Notification title when tool launch fails.
    /// </summary>
    public const string ToolLaunchFailedTitle = "Tool Launch Failed";

    /// <summary>
    /// Notification title when removing a game installation is blocked by active dependent clients.
    /// </summary>
    public const string CannotRemoveInstallationTitle = "Cannot Remove Installation";

    /// <summary>
    /// Notification title when deleting a game installation is blocked by active dependent clients.
    /// </summary>
    public const string CannotDeleteInstallationTitle = "Cannot Delete Installation";

    /// <summary>
    /// Status message format when removing or deleting a game installation is blocked by active dependent clients.
    /// Format arguments: {0} action verb (remove/delete), {1} installation display name, {2} comma-separated client names.
    /// </summary>
    public const string InstallationActionBlockedStatusFormat = "Cannot {0} {1} while dependent game client {2} is active";

    /// <summary>
    /// Notification message format when removing or deleting a game installation is blocked by active dependent clients.
    /// Format arguments: {0} action verb (remove/delete), {1} installation display name, {2} comma-separated client names.
    /// </summary>
    public const string InstallationActionBlockedNotificationFormat = "Cannot {0} game installation '{1}' while dependent game client {2} is active. Please remove the game client first.";

    /// <summary>
    /// Notification title when saving without a required game installation.
    /// </summary>
    public const string MissingGameInstallationTitle = "Missing Game Installation";

    /// <summary>
    /// Notification message when saving without a required game installation.
    /// </summary>
    public const string SelectGameInstallationBeforeSaving = "Please select a game installation before saving.";

    /// <summary>
    /// Notification title when saving without a profile name.
    /// </summary>
    public const string MissingProfileNameTitle = "Missing Name";

    /// <summary>
    /// Notification message when saving without a profile name.
    /// </summary>
    public const string EnterProfileNameBeforeSaving = "Please enter a profile name before saving.";

    /// <summary>
    /// Notification title when content cannot be modified in the current mode.
    /// </summary>
    public const string CannotModifyContentTitle = "Cannot Modify Content";
}
