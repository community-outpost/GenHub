namespace GenHub.Core.Constants;

/// <summary>GitHub-related constants for API interactions, parsing, and UI.</summary>
public static class GitHubConstants
{
    /// <summary>Enumeration for game variants.</summary>
    public enum GameVariant
    {
        /// <summary>Command &amp; Conquer Generals.</summary>
        Generals,

        /// <summary>Command &amp; Conquer Generals Zero Hour.</summary>
        ZeroHour,
    }

    // Build parsing constants

    /// <summary>String identifier for Zero Hour game variant.</summary>
    public const string ZeroHourIdentifier = "ZH";

    /// <summary>String identifier for Generals game variant.</summary>
    public const string GeneralsIdentifier = "Gen";

    /// <summary>String identifier for Debug configuration.</summary>
    public const string DebugConfiguration = "Debug";

    /// <summary>String identifier for Release configuration.</summary>
    public const string ReleaseConfiguration = "Release";

    /// <summary>String identifier for MSVC compiler.</summary>
    public const string MsvcCompiler = "MSVC";

    /// <summary>String identifier for GCC compiler.</summary>
    public const string GccCompiler = "GCC";

    /// <summary>Regex pattern for parsing version strings (e.g., v1.2.3).</summary>
    public const string VersionRegexPattern = @"v(\d+\.\d+\.\d+)";

    /// <summary>Flag identifier for T-flag builds.</summary>
    public const string TFlag = "-T";

    /// <summary>Flag identifier for E-flag builds.</summary>
    public const string EFlag = "-E";

    // File size formatting

    /// <summary>Byte unit identifier.</summary>
    public const string BytesUnit = "B";

    /// <summary>Kilobyte unit identifier.</summary>
    public const string KilobytesUnit = "KB";

    /// <summary>Megabyte unit identifier.</summary>
    public const string MegabytesUnit = "MB";

    /// <summary>Gigabyte unit identifier.</summary>
    public const string GigabytesUnit = "GB";

    /// <summary>Terabyte unit identifier.</summary>
    public const string TerabytesUnit = "TB";

    // Display names and messages

    /// <summary>Default display name for unknown artifacts.</summary>
    public const string UnknownArtifactDisplayName = "Unknown Artifact";

    /// <summary>Default display name for unknown releases.</summary>
    public const string UnknownReleaseDisplayName = "Unknown Release";

    /// <summary>Default display name for unknown workflows.</summary>
    public const string UnknownWorkflowDisplayName = "Unknown Workflow";

    /// <summary>Default fallback content display name.</summary>
    public const string DefaultContentDisplayName = "GitHub Content";

    /// <summary>Default unknown build description.</summary>
    public const string UnknownBuildDescription = "Unknown Build";

    /// <summary>Format string for pull request display names.</summary>
    public const string PullRequestDisplayFormat = "PR #{0}: {1} ({2})";

    /// <summary>Format string for build display names.</summary>
    public const string BuildDisplayFormat = "Build #{0} ({1})";

    /// <summary>Format string for workflow run display names.</summary>
    public const string WorkflowRunDisplayFormat = "Workflow Run #{0}";

    /// <summary>Format string for build descriptions.</summary>
    public const string BuildDescriptionFormat = "{0} {1} ({2})";

    // UI Messages

    /// <summary>Message displayed when no item is selected in details view.</summary>
    public const string SelectItemMessage = "Select an item to view details";

    /// <summary>Message displayed during download operations.</summary>
    public const string DownloadingMessage = "Downloading...";

    /// <summary>Message displayed when download completes successfully.</summary>
    public const string DownloadCompletedMessage = "Download completed";

    /// <summary>Message displayed during installation operations.</summary>
    public const string InstallingMessage = "Installing...";

    /// <summary>Message displayed when installation completes successfully.</summary>
    public const string InstallationCompletedMessage = "Installation completed";

    /// <summary>Format string for download failure messages.</summary>
    public const string DownloadFailedFormat = "Download failed: {0}";

    /// <summary>Format string for installation failure messages.</summary>
    public const string InstallationFailedFormat = "Installation failed: {0}";

    /// <summary>Message displayed when repository loading begins.</summary>
    public const string LoadingRepositoryMessage = "Loading repository...";

    /// <summary>Message displayed when ready for user input.</summary>
    public const string ReadyMessage = "Ready";

    /// <summary>Message displayed for invalid repository URLs.</summary>
    public const string InvalidRepositoryUrlMessage = "Invalid repository URL format";

    /// <summary>Format string for repository loaded messages.</summary>
    public const string RepositoryLoadedFormat = "Loaded {0} items from {1}/{2}";

    /// <summary>Format string for viewing details messages.</summary>
    public const string ViewingDetailsFormat = "Viewing details for {0}";

    /// <summary>Message displayed when installation is cancelled.</summary>
    public const string InstallationCancelledMessage = "Installation cancelled";

    /// <summary>Message displayed for invalid installation parameters.</summary>
    public const string InvalidInstallationParametersMessage = "Invalid installation parameters";

    /// <summary>Format string for starting release asset installation.</summary>
    public const string StartingReleaseAssetInstallationFormat = "Starting installation of release asset: {0}";

    /// <summary>Format string for starting artifact installation.</summary>
    public const string StartingArtifactInstallationFormat = "Starting installation of artifact: {0}";

    /// <summary>Message displayed when download completes successfully.</summary>
    public const string DownloadCompletedSuccessfullyMessage = "Download completed successfully";

    /// <summary>Message displayed when installation completes.</summary>
    public const string InstallationCompletedSuccessfullyMessage = "Installation completed";

    /// <summary>Message displayed when token validation begins.</summary>
    public const string ValidatingTokenMessage = "Validating token...";

    /// <summary>Message displayed when token validation succeeds.</summary>
    public const string TokenValidatedSuccessfullyMessage = "Token validated successfully";

    /// <summary>Message displayed when no token is entered.</summary>
    public const string EnterTokenMessage = "Please enter a token";

    /// <summary>Format string for invalid token messages.</summary>
    public const string InvalidTokenFormat = "Invalid token: {0}";

    /// <summary>Message displayed when tree view is cleared.</summary>
    public const string TreeViewClearedMessage = "Cleared all items from tree view";

    /// <summary>Format string for selected item messages.</summary>
    public const string ItemSelectedFormat = "Selected GitHub item: {0}";

    // UI Labels

    /// <summary>Label for name fields.</summary>
    public const string NameLabel = "Name";

    /// <summary>Label for description fields.</summary>
    public const string DescriptionLabel = "Description";

    /// <summary>Label for type fields.</summary>
    public const string TypeLabel = "Type";

    /// <summary>Label for date fields.</summary>
    public const string DateLabel = "Date";

    /// <summary>Label for release type.</summary>
    public const string ReleaseTypeLabel = "Release Type";

    /// <summary>Label for workflow type.</summary>
    public const string WorkflowTypeLabel = "Workflow Type";

    /// <summary>Label for run number.</summary>
    public const string RunNumberLabel = "Run Number";

    /// <summary>Label for download capability.</summary>
    public const string CanDownloadLabel = "Can Download";

    /// <summary>Label for install capability.</summary>
    public const string CanInstallLabel = "Can Install";

    /// <summary>Label for status fields.</summary>
    public const string StatusLabel = "Status";

    /// <summary>Label for item information section.</summary>
    public const string ItemInformationSectionLabel = "Item Information";

    /// <summary>Label for details header.</summary>
    public const string DetailsHeaderLabel = "Details";

    /// <summary>Label for GitHub manager window title.</summary>
    public const string GitHubManagerTitle = "GitHub Manager";

    /// <summary>Label for installation header.</summary>
    public const string InstallationHeaderLabel = "Installation";

    /// <summary>Label for GitHub authentication dialog title.</summary>
    public const string GitHubAuthenticationTitle = "GitHub Authentication";

    /// <summary>Label for GitHub token dialog header.</summary>
    public const string GitHubTokenHeaderLabel = "GitHub Personal Access Token";

    /// <summary>Label for GitHub manager button.</summary>
    public const string OpenGitHubManagerLabel = "Open GitHub Manager";

    /// <summary>Description for GitHub manager functionality.</summary>
    public const string GitHubManagerDescription = "Browse and manage GitHub repositories, releases, and artifacts";

    /// <summary>Label for repository section.</summary>
    public const string RepositoryLabel = "Repository";

    /// <summary>Tooltip for discover repositories button.</summary>
    public const string DiscoverRepositoriesTooltip = "Discover C&C Repositories";

    // UI Values

    /// <summary>Text for release items.</summary>
    public const string ReleaseItemType = "Release";

    /// <summary>Text for workflow run items.</summary>
    public const string WorkflowRunItemType = "Workflow Run";

    /// <summary>Text for unknown item types.</summary>
    public const string UnknownItemType = "Unknown";

    /// <summary>Text indicating capability is available.</summary>
    public const string CapabilityYes = "Yes";

    /// <summary>Text indicating capability is not available.</summary>
    public const string CapabilityNo = "No";

    // Watermarks and placeholders

    /// <summary>Watermark text for repository URL input.</summary>
    public const string RepositoryUrlWatermark = "Enter GitHub repository URL (e.g., https://github.com/owner/repo)";

    /// <summary>Watermark text for installation path input.</summary>
    public const string InstallationPathWatermark = "Installation directory path";

    /// <summary>Watermark text for token input.</summary>
    public const string TokenWatermark = "ghp_xxxxxxxxxxxxxxxxxxxx";

    // Button labels

    /// <summary>Label for load buttons.</summary>
    public const string LoadButtonLabel = "Load";

    /// <summary>Label for clear buttons.</summary>
    public const string ClearButtonLabel = "Clear";

    /// <summary>Label for download buttons.</summary>
    public const string DownloadButtonLabel = "Download";

    /// <summary>Label for install buttons.</summary>
    public const string InstallButtonLabel = "Install";

    /// <summary>Label for cancel buttons.</summary>
    public const string CancelButtonLabel = "Cancel";

    /// <summary>Label for save buttons.</summary>
    public const string SaveButtonLabel = "Save";

    /// <summary>Label for browse buttons.</summary>
    public const string BrowseButtonLabel = "Browse";

    /// <summary>Label for validate token buttons.</summary>
    public const string ValidateTokenButtonLabel = "Validate Token";

    /// <summary>Label for expand all buttons.</summary>
    public const string ExpandAllButtonLabel = "Expand All";

    /// <summary>Label for collapse all buttons.</summary>
    public const string CollapseAllButtonLabel = "Collapse All";

    // Descriptions

    /// <summary>Description text for GitHub token requirements.</summary>
    public const string GitHubTokenDescription = "Enter your GitHub Personal Access Token to access private repositories and increase rate limits:";

    /// <summary>Message displayed when no installation is in progress.</summary>
    public const string NoInstallationInProgressMessage = "No installation in progress. Select an item to install from the tree view.";

    /// <summary>Message displayed when ready to install.</summary>
    public const string ReadyToInstallMessage = "Ready to install";

    /// <summary>Message displayed when no item is selected.</summary>
    public const string NoItemSelectedMessage = "No item selected";

    /// <summary>Format string for ready to install messages.</summary>
    public const string ReadyToInstallItemFormat = "Ready to install {0}";

    /// <summary>Display name for releases folder in tree view.</summary>
    public const string ReleasesFolderName = "Releases";

    /// <summary>Display name for workflow builds folder in tree view.</summary>
    public const string WorkflowBuildsFolderName = "Workflow Builds";

    // Resolver IDs

    /// <summary>Resolver ID for GitHub release resolver.</summary>
    public const string GitHubReleaseResolverId = "GitHubRelease";

    /// <summary>Resolver ID for GitHub artifact resolver.</summary>
    public const string GitHubArtifactResolverId = "GitHubArtifact";

    // Publisher ID prefix

    /// <summary>Prefix for GitHub publisher IDs.</summary>
    public const string PublisherIdPrefix = "github-";

    // Component descriptions

    /// <summary>Description for GitHub releases discoverer.</summary>
    public const string GitHubReleasesDiscovererDescription = "Discovers content from GitHub releases";

    /// <summary>Description for GitHub content deliverer.</summary>
    public const string GitHubDelivererDescription = "Delivers GitHub content including release archives";

    // Metadata keys

    /// <summary>Metadata key for repository owner.</summary>
    public const string OwnerMetadataKey = "owner";

    /// <summary>Metadata key for repository name.</summary>
    public const string RepoMetadataKey = "repo";

    /// <summary>Metadata key for release tag.</summary>
    public const string TagMetadataKey = "tag";
}
