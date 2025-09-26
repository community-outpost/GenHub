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
    /// The First Decade installation of the game.
    /// </summary>
    TheFirstDecade = 3,

    /// <summary>
    /// CD ISO installation of the game.
    /// </summary>
    CDISO = 4,

    /// <summary>
    /// Wine/Proton installation on Linux.
    /// </summary>
    Wine = 5,

    /// <summary>
    /// Manual/retail installation.
    /// </summary>
    Retail = 6,
}
