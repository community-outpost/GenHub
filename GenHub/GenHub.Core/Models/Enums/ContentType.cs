namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the type of content in a manifest.
/// </summary>
public enum ContentType
{
    // Foundation types (detected/installed)

    /// <summary>EA/Steam/Origin installation.</summary>
    BaseGame,

    /// <summary>Independent game executable.</summary>
    StandaloneVersion,

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
}
