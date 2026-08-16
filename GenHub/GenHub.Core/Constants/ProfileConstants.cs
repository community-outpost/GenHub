namespace GenHub.Core.Constants;

/// <summary>
/// General constants for profiles.
/// </summary>
public static class ProfileConstants
{
    /// <summary>
    /// The maximum allowed length for a profile name.
    /// </summary>
    public const int MaxProfileNameLength = 100;

    /// <summary>
    /// The default profile name used for new profiles.
    /// </summary>
    public const string DefaultProfileName = "New Profile";

    /// <summary>
    /// The workspace ID used for tool profiles.
    /// </summary>
    public const string ToolProfileWorkspaceId = "tool-profile";

    /// <summary>
    /// The prefix used for tool workspace IDs.
    /// </summary>
    public const string ToolProfileWorkspaceIdPrefix = "tool";

    /// <summary>
    /// The suffix used for profile copy names.
    /// </summary>
    public const string CopyNameSuffix = "(Copy)";

    /// <summary>
    /// The format string used for numbered profile copy names.
    /// </summary>
    public const string CopyNameNumberedFormat = "(Copy {0})";

    /// <summary>
    /// The prefix used for descriptions of profiles created with specific content.
    /// </summary>
    public const string CreatedWithContentDescriptionPrefix = "Profile created with ";

    /// <summary>
    /// Localization key for workspace preparation notification title.
    /// </summary>
    public const string WorkspacePreparingTitleKey = "GameProfiles.Launch.WorkspacePreparingTitle";

    /// <summary>
    /// Default fallback title for workspace preparation notification.
    /// </summary>
    public const string WorkspacePreparingDefaultTitle = "Preparing Workspace";

    /// <summary>
    /// Localization key for workspace initialization notification message.
    /// </summary>
    public const string WorkspaceInitializingMessageKey = "GameProfiles.Launch.WorkspaceInitializingMessage";

    /// <summary>
    /// Default fallback format for workspace initialization notification message.
    /// </summary>
    public const string WorkspaceInitializingDefaultFormat = "Initializing workspace for '{0}'. First launch may take a moment while files are set up...";

    /// <summary>
    /// Localization key for workspace error notification title.
    /// </summary>
    public const string WorkspaceErrorTitleKey = "GameProfiles.Launch.WorkspaceErrorTitle";

    /// <summary>
    /// Default fallback title for workspace error notification.
    /// </summary>
    public const string WorkspaceErrorDefaultTitle = "Workspace Error";

    /// <summary>
    /// Localization key for workspace failed notification message.
    /// </summary>
    public const string WorkspaceFailedMessageKey = "GameProfiles.Launch.WorkspaceFailedMessage";

    /// <summary>
    /// Default fallback format for workspace failed notification message.
    /// </summary>
    public const string WorkspaceFailedDefaultFormat = "Workspace initialization failed for '{0}'.";

    /// <summary>
    /// Default name for new profiles.
    /// </summary>
    public const string DefaultProfileName = "New Profile";
}
