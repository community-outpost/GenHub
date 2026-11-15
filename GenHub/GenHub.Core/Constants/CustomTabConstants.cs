namespace GenHub.Core.Constants;

/// <summary>
/// Limits, allowlists, link patterns and UI resource keys for publisher-defined custom tabs.
/// </summary>
public static class CustomTabConstants
{
    /// <summary>
    /// The maximum number of custom tabs a single content item or the catalog root may declare.
    /// </summary>
    public const int MaxTabsPerScope = 10;

    /// <summary>
    /// The maximum number of cards a single custom tab may declare.
    /// </summary>
    public const int MaxCardsPerTab = 24;

    /// <summary>
    /// The maximum length of a custom tab identifier.
    /// </summary>
    public const int MaxTabIdLength = 64;

    /// <summary>
    /// The maximum length of a custom tab header.
    /// </summary>
    public const int MaxHeaderLength = 40;

    /// <summary>
    /// The maximum length of a custom tab intro.
    /// </summary>
    public const int MaxIntroLength = 2000;

    /// <summary>
    /// The maximum length of a custom tab Markdown body.
    /// </summary>
    public const int MaxBodyLength = 50000;

    /// <summary>
    /// The maximum length of a card title or label.
    /// </summary>
    public const int MaxCardTitleLength = 100;

    /// <summary>
    /// The maximum length of a card description.
    /// </summary>
    public const int MaxCardDescriptionLength = 1000;

    /// <summary>
    /// The URI scheme for images bundled with the application.
    /// </summary>
    public const string AvaloniaResourceScheme = "avares";

    /// <summary>
    /// The prefix of a generated tab ID when the header yields no usable characters.
    /// </summary>
    public const string GeneratedIdFallbackPrefix = "tab-";

    /// <summary>
    /// The number of hash characters in a generated fallback tab ID.
    /// </summary>
    public const int GeneratedIdFallbackHashLength = 8;

    /// <summary>
    /// The localization key for the edit button tooltip on a custom tab header.
    /// </summary>
    public const string EditTabLocalizationKey = "Tools.PublisherStudio.Content.CustomTabs.EditTab";

    /// <summary>
    /// The localization key for the delete button tooltip on a custom tab header.
    /// </summary>
    public const string DeleteTabLocalizationKey = "Tools.PublisherStudio.Content.CustomTabs.DeleteTab";

    /// <summary>
    /// The resource key of the edit icon on a custom tab header.
    /// </summary>
    public const string EditIconResourceKey = "EditIcon";

    /// <summary>
    /// The resource key of the delete icon on a custom tab header.
    /// </summary>
    public const string DeleteIconResourceKey = "DeleteIcon";

    /// <summary>
    /// The host of Google Drive file viewer links.
    /// </summary>
    public const string GoogleDriveHost = "drive.google.com";

    /// <summary>
    /// The direct download URL for a Google Drive file. The placeholder is the escaped file ID.
    /// </summary>
    public const string GoogleDriveDirectUrlFormat = "https://drive.usercontent.google.com/download?id={0}&export=view";

    /// <summary>
    /// The Google Drive open link path.
    /// </summary>
    public const string GoogleDriveOpenPath = "/open";

    /// <summary>
    /// The Google Drive legacy download link path.
    /// </summary>
    public const string GoogleDriveUcPath = "/uc";

    /// <summary>
    /// The query parameter that carries a Google Drive file ID.
    /// </summary>
    public const string GoogleDriveIdQueryKey = "id";

    /// <summary>
    /// The query parameter that carries a Google Drive resource key.
    /// </summary>
    public const string GoogleDriveResourceKeyQueryKey = "resourcekey";

    /// <summary>
    /// Matches a Google Drive file viewer path and captures the file ID in the <c>id</c> group.
    /// </summary>
    public const string GoogleDriveFilePathPattern = @"^(?:/u/\d+)?/file/d/(?<id>[A-Za-z0-9_-]{10,})(?:/(?:view|preview|edit)?)?/?$";

    /// <summary>
    /// Matches a Google Drive file ID.
    /// </summary>
    public const string GoogleDriveFileIdPattern = @"^[A-Za-z0-9_-]{10,}$";

    /// <summary>
    /// Matches a Google Drive resource key.
    /// </summary>
    public const string GoogleDriveResourceKeyPattern = @"^[A-Za-z0-9_-]+$";

    /// <summary>
    /// The Discord CDN host for attachments.
    /// </summary>
    public const string DiscordCdnHost = "cdn.discordapp.com";

    /// <summary>
    /// The Discord media proxy host for attachments.
    /// </summary>
    public const string DiscordMediaHost = "media.discordapp.net";

    /// <summary>
    /// The path prefix of Discord attachment links.
    /// </summary>
    public const string DiscordAttachmentsPathPrefix = "/attachments/";

    /// <summary>
    /// The path prefix of Discord ephemeral attachment links.
    /// </summary>
    public const string DiscordEphemeralAttachmentsPathPrefix = "/ephemeral-attachments/";

    /// <summary>
    /// The query parameter that carries a Discord attachment's expiry time.
    /// </summary>
    public const string DiscordExpiryQueryKey = "ex";

    /// <summary>
    /// The query parameter that carries a Discord attachment's signature.
    /// </summary>
    public const string DiscordSignatureQueryKey = "hm";

    /// <summary>
    /// Matches an http or https link in a tab body.
    /// </summary>
    public const string BodyLinkPattern = @"https?://[^\s)\]""'<>]+";
}
