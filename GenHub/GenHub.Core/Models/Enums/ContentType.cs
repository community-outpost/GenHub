namespace GenHub.Core.Models.Enums;

using System.Text.Json.Serialization;

/// <summary>
/// Defines the type of content in a manifest.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
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

    /// <summary>UI customization skins (e.g., Winamp skins).</summary>
    Skin,

    /// <summary>Video content (trailers, gameplay recordings).</summary>
    Video,

    /// <summary>Game replay files.</summary>
    Replay,

    /// <summary>Screensaver files.</summary>
    Screensaver,

    /// <summary>Standalone executable file.</summary>
    Executable,

    /// <summary>Modding and mapping tools/utilities.</summary>
    ModdingTool,

    /// <summary>Unknown content type.</summary>
    UnknownContentType,
}
