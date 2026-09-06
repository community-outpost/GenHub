using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

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
    /// Navigation action ID for Game Detection guide.
    /// </summary>
    public const string ActionNavScanGames = "NAV_INFO_scan-games";

    /// <summary>
    /// Navigation action ID for Downloads tab.
    /// </summary>
    public const string ActionNavDownloads = "NAV_Downloads";

    /// <summary>
    /// Navigation action ID for Content guide.
    /// </summary>
    public const string ActionNavGameProfileContent = "NAV_INFO_game-profile-content";

    /// <summary>
    /// Navigation action ID for Local Content guide.
    /// </summary>
    public const string ActionNavLocalContent = "NAV_INFO_local-content";

    /// <summary>
    /// Navigation action ID for Settings tab.
    /// </summary>
    public const string ActionNavSettings = "NAV_Settings";

    /// <summary>
    /// Navigation action ID for App Updates guide.
    /// </summary>
    public const string ActionNavAppUpdates = "NAV_INFO_app-updates";

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
    /// Icon key for Update.
    /// </summary>
    public const string IconUpdate = "Update";

    /// <summary>
    /// The list of supported languages for the FAQ.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedFaqLanguages = new[]
    {
        "en", "de", "ph", "ar",
    };
}
