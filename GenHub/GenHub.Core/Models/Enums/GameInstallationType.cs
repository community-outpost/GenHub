namespace GenHub.Core.Models.Enums;

/// <summary>
/// Type of Game Installation.
/// </summary>
public enum GameInstallationType
{
    /// <summary>
    /// Unrecognized game installation.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Steam installation of the game.
    /// </summary>
    Steam = 1,

    /// <summary>
    /// EA App installation of the game.
    /// </summary>
    EaApp = 2,

    /// <summary>
    /// Origin installation of the game.
    /// </summary>
    Origin = 3,

    /// <summary>
    /// The First Decade installation of the game.
    /// </summary>
    TheFirstDecade = 4,

    /// <summary>
    /// RGMechanics installation of the game.
    /// </summary>
    RGMechanics = 5,

    /// <summary>
    /// CD ISO installation of the game.
    /// </summary>
    CDISO = 6,

    /// <summary>
    /// Wine/Proton installation on Linux.
    /// </summary>
    Wine = 7,

    /// <summary>
    /// Manual/retail installation.
    /// </summary>
    Retail = 8,
}
