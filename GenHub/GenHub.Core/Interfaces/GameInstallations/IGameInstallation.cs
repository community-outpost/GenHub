namespace GenHub.Core;

/// <summary>
/// Interface for a game installation.
/// </summary>
public interface IGameInstallation
{
    /// <summary>
    /// Gets the type of game installation.
    /// </summary>
    public GameInstallationType InstallationType { get; }

    /// <summary>
    /// Gets a value indicating whether the vanilla game is installed.
    /// </summary>
    public bool IsVanillaInstalled { get; }

    /// <summary>
    /// Gets the path of the vanilla game installation.
    /// </summary>
    public string VanillaGamePath { get; }

    /// <summary>
    /// Gets a value indicating whether Zero Hour is installed.
    /// </summary>
    public bool IsZeroHourInstalled { get; }

    /// <summary>
    /// Gets the path of the Zero Hour installation.
    /// </summary>
    public string ZeroHourGamePath { get; }

    /// <summary>
    /// Fetch the game installations.
    /// </summary>
    public void Fetch();
}