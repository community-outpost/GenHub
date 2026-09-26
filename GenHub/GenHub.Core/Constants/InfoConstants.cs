using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the Info and FAQ features.
/// </summary>
[SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Centralized URI constants / mock demo paths")]
public static class InfoConstants
{
    /// <summary>
    /// The base URL for the FAQ page.
    /// </summary>
    public const string FaqBaseUrl = "https://legi.cc/bugs-solutions-and-faq/";

    /// <summary>
    /// The default language for FAQs.
    /// </summary>
    public const string FaqDefaultLanguage = "en";

    /// <summary>
    /// Module name for GenHub Guide.
    /// </summary>
    public const string ModuleGuide = "GenHub Guide";

    /// <summary>
    /// Module name for Zero Hour.
    /// </summary>
    public const string ModuleZeroHour = "Zero Hour";

    /// <summary>
    /// Module name for GeneralsOnline.
    /// </summary>
    public const string ModuleGeneralsOnline = "GeneralsOnline";

    /// <summary>
    /// Section ID for FAQ.
    /// </summary>
    public const string SectionFaq = "faq";

    /// <summary>
    /// Section ID for GeneralsOnline changelog.
    /// </summary>
    public const string SectionGoChangelog = "go-changelog";

    /// <summary>
    /// Section ID for Quickstart guide.
    /// </summary>
    public const string SectionQuickstart = "quickstart";

    /// <summary>
    /// Section ID for Game Profiles guide.
    /// </summary>
    public const string SectionGameProfiles = "game-profiles";

    /// <summary>
    /// Section ID for Game Settings guide.
    /// </summary>
    public const string SectionGameSettings = "game-settings";

    /// <summary>
    /// Section ID for Game Profile Content guide.
    /// </summary>
    public const string SectionGameProfileContent = "game-profile-content";

    /// <summary>
    /// Section ID for Shortcuts guide.
    /// </summary>
    public const string SectionShortcuts = "shortcuts";

    /// <summary>
    /// Section ID for Steam Integration guide.
    /// </summary>
    public const string SectionSteam = "steam-integration";

    /// <summary>
    /// Section ID for Local Content guide.
    /// </summary>
    public const string SectionLocalContent = "local-content";

    /// <summary>
    /// Section ID for Tools guide.
    /// </summary>
    public const string SectionTools = "tools";

    /// <summary>
    /// Section ID for Game Detection / Scan for Games guide.
    /// </summary>
    public const string SectionScanGames = "scan-games";

    /// <summary>
    /// Section ID for Virtual Workspaces guide.
    /// </summary>
    public const string SectionWorkspaces = "workspaces";

    /// <summary>
    /// Section ID for Application Updates guide.
    /// </summary>
    public const string SectionAppUpdates = "app-updates";

    /// <summary>
    /// Section ID for Changelogs.
    /// </summary>
    public const string SectionChangelogs = "changelogs";

    /// <summary>
    /// File name for the cached GitHub changelogs.
    /// </summary>
    public const string ChangelogsCacheFileName = "changelogs-cache.json";

    /// <summary>
    /// Action identifier to navigate to the Game Profiles tab.
    /// </summary>
    public const string ActionNavGameProfiles = "NAV_GameProfiles";

    /// <summary>
    /// Action identifier to navigate to the Downloads tab.
    /// </summary>
    public const string ActionNavDownloads = "NAV_Downloads";

    /// <summary>
    /// Action identifier to navigate to the Add Local Content wizard.
    /// </summary>
    public const string ActionNavAddContent = "NAV_AddContent";

    /// <summary>
    /// Action identifier to navigate to the Settings tab.
    /// </summary>
    public const string ActionNavSettings = "NAV_Settings";

    /// <summary>
    /// Action identifier to open the Virtual Workspaces info section.
    /// </summary>
    public const string ActionNavInfoWorkspace = "NAV_INFO_workspace";

    /// <summary>
    /// Action identifier to open the Tools tab.
    /// </summary>
    public const string ActionNavTools = "NAV_Tools";

    /// <summary>
    /// Navigation action ID for Game Detection guide.
    /// </summary>
    public const string ActionNavScanGames = "NAV_INFO_scan-games";

    /// <summary>
    /// Navigation action ID for Content guide.
    /// </summary>
    public const string ActionNavGameProfileContent = "NAV_INFO_game-profile-content";

    /// <summary>
    /// Navigation action ID for Local Content guide.
    /// </summary>
    public const string ActionNavLocalContent = "NAV_INFO_local-content";

    /// <summary>
    /// Navigation action ID for App Updates guide.
    /// </summary>
    public const string ActionNavAppUpdates = "NAV_INFO_app-updates";

    /// <summary>
    /// Localization string key for the info cards right sidebar title.
    /// </summary>
    public const string StringInfoSidebarCardsTitle = "Info.Sidebar.CardsTitle";

    /// <summary>
    /// Card ID for Quickstart Welcome.
    /// </summary>
    public const string CardQuickstartWelcome = "welcome";

    /// <summary>
    /// Card ID for Quickstart Step 1 Scan.
    /// </summary>
    public const string CardQuickstartStep1Scan = "step1-scan";

    /// <summary>
    /// Card ID for Quickstart Step 2 Downloads.
    /// </summary>
    public const string CardQuickstartStep2Downloads = "step2-downloads";

    /// <summary>
    /// Card ID for Quickstart Step 3 Local Content.
    /// </summary>
    public const string CardQuickstartStep3LocalContent = "step3-local-content";

    /// <summary>
    /// Card ID for Quickstart Step 4 Play.
    /// </summary>
    public const string CardQuickstartStep4Play = "step4-play";

    /// <summary>
    /// Card ID for Downloads Standard Generals / Zero Hour.
    /// </summary>
    public const string CardDownloadsStandardAv = "standard-av";

    /// <summary>
    /// Card ID for Downloads TheSuperHackers Community Patch.
    /// </summary>
    public const string CardDownloadsSuperHackers = "superhackers";

    /// <summary>
    /// Card ID for Downloads Generals Online.
    /// </summary>
    public const string CardDownloadsGeneralsOnline = "generals-online";

    /// <summary>
    /// Card ID for Profiles Hierarchy.
    /// </summary>
    public const string CardProfilesHierarchy = "hierarchy";

    /// <summary>
    /// Card ID for Profiles Cloning.
    /// </summary>
    public const string CardProfilesCloning = "cloning";

    /// <summary>
    /// Card ID for Profiles Editor.
    /// </summary>
    public const string CardProfilesEditor = "editor";

    /// <summary>
    /// Card ID for Profiles Virtual File System.
    /// </summary>
    public const string CardProfilesVfs = "vfs";

    /// <summary>
    /// Card ID for Profiles Controls.
    /// </summary>
    public const string CardProfilesControls = "controls";

    /// <summary>
    /// Card ID for Profiles Advanced Options.
    /// </summary>
    public const string CardProfilesAdvancedOptions = "advanced-options";

    /// <summary>
    /// Card ID for Content Importing.
    /// </summary>
    public const string CardContentImporting = "importing";

    /// <summary>
    /// Card ID for Content Executable Selection.
    /// </summary>
    public const string CardContentExecutableSelection = "executable-selection";

    /// <summary>
    /// Card ID for Content GenLauncher Normalization.
    /// </summary>
    public const string CardContentGenLauncherNormalization = "genlauncher-normalization";

    /// <summary>
    /// Card ID for Workspace Isolation.
    /// </summary>
    public const string CardWorkspacesIsolation = "workspace-isolation";

    /// <summary>
    /// Card ID for Workspace Magic Mirror.
    /// </summary>
    public const string CardWorkspacesMagicMirror = "magic-mirror";

    /// <summary>
    /// Card ID for Workspace Strategies.
    /// </summary>
    public const string CardWorkspacesStrategies = "strategies";

    /// <summary>
    /// Card ID for Workspace Deep Dive.
    /// </summary>
    public const string CardWorkspacesDeepDive = "deep-dive";

    /// <summary>
    /// Card ID for Workspace Troubleshooting.
    /// </summary>
    public const string CardWorkspacesTroubleshooting = "troubleshooting";

    /// <summary>
    /// Card ID for Workspace Performance.
    /// </summary>
    public const string CardWorkspacesPerformance = "performance";

    /// <summary>
    /// Card ID for Settings Sandbox.
    /// </summary>
    public const string CardSettingsSandbox = "sandbox";

    /// <summary>
    /// Card ID for Settings Controls.
    /// </summary>
    public const string CardSettingsControls = "controls";

    /// <summary>
    /// Card ID for Settings Advanced Options.
    /// </summary>
    public const string CardSettingsAdvancedOptions = "advanced-options";

    /// <summary>
    /// Card ID for Settings Headless Mode.
    /// </summary>
    public const string CardSettingsHeadless = "headless";

    /// <summary>
    /// Card ID for Shortcuts Creation.
    /// </summary>
    public const string CardShortcutsCreation = "creation";

    /// <summary>
    /// Card ID for Shortcuts Icons.
    /// </summary>
    public const string CardShortcutsIcons = "icons";

    /// <summary>
    /// Card ID for Steam App ID.
    /// </summary>
    public const string CardSteamAppId = "appid";

    /// <summary>
    /// Card ID for Steam Requirements.
    /// </summary>
    public const string CardSteamRequirements = "requirements";

    /// <summary>
    /// Card ID for Steam Time Tracking.
    /// </summary>
    public const string CardSteamTimeTracking = "time-tracking";

    /// <summary>
    /// Card ID for Tools Replay Import.
    /// </summary>
    public const string CardToolsReplayImport = "replay-import";

    /// <summary>
    /// Card ID for Tools Replay Game Client Mapping.
    /// </summary>
    public const string CardToolsReplayGameClientMapping = "replay-gameclient-mapping";

    /// <summary>
    /// Card ID for Tools Replay Checkpoints &amp; Takeover.
    /// </summary>
    public const string CardToolsReplayCheckpointsTakeover = "replay-checkpoints-takeover";

    /// <summary>
    /// Card ID for Tools Replay Cloud Sharing.
    /// </summary>
    public const string CardToolsReplayCloud = "replay-cloud";

    /// <summary>
    /// Card ID for Tools Replay Archive.
    /// </summary>
    public const string CardToolsReplayArchive = "replay-archive";

    /// <summary>
    /// Card ID for Tools Map Library.
    /// </summary>
    public const string CardToolsMapLibrary = "map-library";

    /// <summary>
    /// Card ID for Tools Map Packs.
    /// </summary>
    public const string CardToolsMapPacks = "map-packs";

    /// <summary>
    /// Card ID for Tools Hotkey Editor Rebind.
    /// </summary>
    public const string CardToolsHotkeyEditorRebind = "hotkey-editor-rebind";

    /// <summary>
    /// Card ID for Tools Hotkey Editor Addons.
    /// </summary>
    public const string CardToolsHotkeyEditorAddons = "hotkey-editor-addons";

    /// <summary>
    /// Card ID for Tools Publisher Studio 3-Tier Pipeline.
    /// </summary>
    public const string CardToolsPublisherStudioPipeline = "publisher-studio-pipeline";

    /// <summary>
    /// Card ID for Tools Publisher Studio CDN Hosting.
    /// </summary>
    public const string CardToolsPublisherStudioCdnHosting = "publisher-studio-cdn-hosting";

    /// <summary>
    /// Card ID for Tools Publisher Studio Update Notifications.
    /// </summary>
    public const string CardToolsPublisherStudioUpdateNotifications = "publisher-studio-update-notifications";

    /// <summary>
    /// Card ID for Tools Publisher Studio Authoring.
    /// </summary>
    public const string CardToolsPublisherStudioAuthoring = "publisher-studio-authoring";

    /// <summary>
    /// Card ID for Tools ModBuilder Suite Pipeline.
    /// </summary>
    public const string CardToolsModBuilderSuitePipeline = "modbuilder-suite-pipeline";

    /// <summary>
    /// Alias for CardToolsModBuilderSuitePipeline.
    /// </summary>
    public const string CardToolsModbuilderSuitePipeline = CardToolsModBuilderSuitePipeline;

    /// <summary>
    /// Card ID for Tools ModBuilder Suite Window Build.
    /// </summary>
    public const string CardToolsModBuilderSuiteWndBuild = "modbuilder-suite-wnd-build";

    /// <summary>
    /// Alias for CardToolsModBuilderSuiteWndBuild.
    /// </summary>
    public const string CardToolsModbuilderSuiteWndBuild = CardToolsModBuilderSuiteWndBuild;

    /// <summary>
    /// Card ID for Scan Games Auto Detection.
    /// </summary>
    public const string CardScanAutoDetection = "auto-detection";

    /// <summary>
    /// Card ID for Scan Games Signature Verification.
    /// </summary>
    public const string CardScanSignatureVerification = "signature-verification";

    /// <summary>
    /// Card ID for Scan Games Cross Platform Detection.
    /// </summary>
    public const string CardScanCrossPlatformDetection = "cross-platform-detection";

    /// <summary>
    /// Card ID for Updates Version Control.
    /// </summary>
    public const string CardUpdatesVersionControl = "version-control";

    /// <summary>
    /// Card ID for Updates Workflow.
    /// </summary>
    public const string CardUpdatesWorkflow = "workflow";

    /// <summary>
    /// Card ID for Updates Rollback.
    /// </summary>
    public const string CardUpdatesRollback = "rollback";

    /// <summary>
    /// Card ID for Updates GitHub OAuth Device Flow.
    /// </summary>
    public const string CardUpdatesGitHubOAuthDevice = "github-oauth-device";

    /// <summary>
    /// Alias for CardUpdatesGitHubOAuthDevice.
    /// </summary>
    public const string CardUpdatesGithubOauthDevice = CardUpdatesGitHubOAuthDevice;

    /// <summary>
    /// Card ID for Updates CI Artifacts and PR Testing.
    /// </summary>
    public const string CardUpdatesCiArtifactsPrTesting = "ci-artifacts-pr-testing";

    /// <summary>
    /// Card ID for Updates Offline Downloads.
    /// </summary>
    public const string CardUpdatesOfflineDownloads = "offline-downloads";

    /// <summary>
    /// Card ID for Changelog Overview.
    /// </summary>
    public const string CardChangelogsOverview = "changelogs-overview";

    /// <summary>
    /// Card ID for Changelog Updates.
    /// </summary>
    public const string CardChangelogsUpdates = "changelogs-updates";

    /// <summary>
    /// Card ID for Changelog Compatibility.
    /// </summary>
    public const string CardChangelogsCompatibility = "changelogs-compatibility";

    /// <summary>
    /// Prefix for FAQ Card IDs.
    /// </summary>
    public const string CardFaqPrefix = "faq-";

    /// <summary>
    /// Icon key for Magnify / Search.
    /// </summary>
    public const string IconMagnify = "Magnify";

    /// <summary>
    /// Icon key for Cloud Download.
    /// </summary>
    public const string IconCloudDownload = "CloudDownload";

    /// <summary>
    /// Icon key for Book / Guide.
    /// </summary>
    public const string IconBookOpenVariant = "BookOpenVariant";

    /// <summary>
    /// Icon key for Folder Upload.
    /// </summary>
    public const string IconFolderUpload = "FolderUpload";

    /// <summary>
    /// Icon key for Harddisk / Storage.
    /// </summary>
    public const string IconHarddisk = "Harddisk";

    /// <summary>
    /// Icon key for Web / External link.
    /// </summary>
    public const string IconWeb = "Web";

    /// <summary>
    /// Icon key for Arrow Right.
    /// </summary>
    public const string IconArrowRight = "ArrowRight";

    /// <summary>
    /// Icon key for Shield Check.
    /// </summary>
    public const string IconShieldCheck = "ShieldCheck";

    /// <summary>
    /// Icon key for View Dashboard.
    /// </summary>
    public const string IconViewDashboard = "ViewDashboard";

    /// <summary>
    /// Icon key for Folder Open.
    /// </summary>
    public const string IconFolderOpen = "FolderOpen";

    /// <summary>
    /// Icon key for Information.
    /// </summary>
    public const string IconInformation = "Information";

    /// <summary>
    /// Icon key for Download.
    /// </summary>
    public const string IconDownload = "Download";

    /// <summary>
    /// Icon key for Cog / Settings.
    /// </summary>
    public const string IconCog = "Cog";

    /// <summary>
    /// Icon key for Controller.
    /// </summary>
    public const string IconController = "Controller";

    /// <summary>
    /// Icon key for Content Cut.
    /// </summary>
    public const string IconContentCut = "ContentCut";

    /// <summary>
    /// Icon key for Wrench / Tools.
    /// </summary>
    public const string IconWrench = "Wrench";

    /// <summary>
    /// Icon key for Radar / Scan.
    /// </summary>
    public const string IconRadar = "Radar";

    /// <summary>
    /// Icon key for Update.
    /// </summary>
    public const string IconUpdate = "Update";

    /// <summary>
    /// Icon key for History.
    /// </summary>
    public const string IconHistory = "History";

    /// <summary>
    /// Icon key for Help Circle / FAQ.
    /// </summary>
    public const string IconHelpCircle = "HelpCircle";

    /// <summary>
    /// Icon key for Web Sync / Online.
    /// </summary>
    public const string IconWebSync = "WebSync";

    /// <summary>
    /// Icon resource URI for Generals demo item.
    /// </summary>
    public const string DemoIconGenerals = "avares://GenHub/Assets/Icons/generals-icon.png";

    /// <summary>
    /// Icon resource URI for Zero Hour demo item.
    /// </summary>
    public const string DemoIconZeroHour = "avares://GenHub/Assets/Icons/zerohour-icon.png";

    /// <summary>
    /// Default fallback base installation directory for simulated demo items.
    /// </summary>
    public const string DemoFallbackBasePath = @"C:\Program Files (x86)";

    /// <summary>
    /// Demo directory folder name for EA Games.
    /// </summary>
    public const string DemoFolderEaGames = "EA Games";

    /// <summary>
    /// Demo directory folder name for Command and Conquer Generals.
    /// </summary>
    public const string DemoFolderGenerals = "Command and Conquer Generals";

    /// <summary>
    /// Demo directory folder name for Command and Conquer Generals Zero Hour.
    /// </summary>
    public const string DemoFolderZeroHour = "Command and Conquer Generals Zero Hour";

    /// <summary>
    /// Demo source path for ShockWave mod zip archive.
    /// </summary>
    public static readonly string DemoModSourcePath = Path.Combine(DemoFallbackBasePath, "Downloads", "ShockWave_v1.201.zip");

    /// <summary>
    /// Demo base folder for ShockWave mod files.
    /// </summary>
    public static readonly string DemoModBasePath = Path.Combine(DemoFallbackBasePath, "Demo", "ShockWave_v1.201");

    /// <summary>
    /// Demo source path for TheSuperHackers game client directory.
    /// </summary>
    public static readonly string DemoGameClientSourcePath = Path.Combine(DemoFallbackBasePath, "Engines", "TheSuperHackers_ZeroHour_test_build");

    /// <summary>
    /// Demo source path for GenHotkeys modding tool directory.
    /// </summary>
    public static readonly string DemoModdingToolSourcePath = Path.Combine(DemoFallbackBasePath, "Tools", "GenHotkeys_v2.1");

    /// <summary>
    /// Demo source path for WorldBuilder executable.
    /// </summary>
    public static readonly string DemoExecutableSourcePath = Path.Combine(DemoFallbackBasePath, DemoFolderEaGames, DemoFolderZeroHour, "WorldBuilder.exe");

    /// <summary>
    /// Demo base folder for WorldBuilder files.
    /// </summary>
    public static readonly string DemoExecutableBasePath = Path.Combine(DemoFallbackBasePath, "Demo", "WorldBuilder_ZH");

    /// <summary>
    /// The list of supported languages for the FAQ.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedFaqLanguages =
    [
        "en", "de", "ph", "ar",
    ];
}
