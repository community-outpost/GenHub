namespace GenHub.Core.Models.Enums;

/// <summary>
/// Content publisher types for display and attribution.
/// </summary>
public enum Publisher
{
    /// <summary>Unknown or unspecified publisher.</summary>
    Unknown = 0,

    /// <summary>Steam platform publisher.</summary>
    Steam = 1,

    /// <summary>EA App publisher.</summary>
    EaApp = 2,

    /// <summary>The First Decade publisher.</summary>
    TheFirstDecade = 3,

    /// <summary>Wine/Proton compatibility layer.</summary>
    Wine = 4,

    /// <summary>CD-ROM installation.</summary>
    CdRom = 5,

    /// <summary>Retail installation.</summary>
    Retail = 6,

    /// <summary>Generals Online community client.</summary>
    GeneralsOnline = 7,

    /// <summary>The Super Hackers community.</summary>
    SuperHackers = 8,

    /// <summary>CNC Labs community.</summary>
    CncLabs = 9,

    /// <summary>AODMaps community.</summary>
    AODMaps = 10,
}
