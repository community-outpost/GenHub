namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the type of content in a manifest.
/// </summary>
public enum ContentType
{
    // Foundation types (detected/installed)

    /// <summary>EA/Steam/Disk installation.</summary>
    GameInstallation,

    /// <summary>Independent game executable.</summary>
    GameClient,

    // Content types (built on foundation)

    /// <summary>Major gameplay changes.</summary>
    Mod,

    /// <summary>Balance/configuration changes.</summary>
    Patch,

    /// <summary>Utilities/tools.</summary>
    Addon,

    /// <summary>Map collections.</summary>
    MapPack,

    /// <summary>Localization.</summary>
    LanguagePack,

    // Meta types

    /// <summary>Collection of multiple contents.</summary>
    ContentBundle,

    /// <summary>Link to other publisher content.</summary>
    PublisherReferral,

    /// <summary>Link to specific content.</summary>
    ContentReferral,

    /// <summary>Story-driven gameplay with objectives.</summary>
    Mission,

    /// <summary>Free-play or skirmish mode on a map.</summary>
    Map,

    /// <summary>Unknown content type</summary>
    UnknownContentType,
}