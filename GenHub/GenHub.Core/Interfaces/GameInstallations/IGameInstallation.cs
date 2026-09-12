using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;

namespace GenHub.Core.Interfaces.GameInstallations;

/// <summary>
/// Interface for a game installation.
/// </summary>
public interface IGameInstallation
{
    /// <summary>
    /// Gets the unique identifier for this installation.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the display name for this installation.
    /// </summary>
    string DisplayName => InstallationType.GetDisplayName();

    /// <summary>
    /// Gets the type of game installation.
    /// </summary>
    GameInstallationType InstallationType { get; }

    /// <summary>
    /// Gets the base installation directory path.
    /// </summary>
    string InstallationPath { get; }

    /// <summary>
    /// Gets a value indicating whether Generals (vanilla) is installed in this installation.
    /// </summary>
    bool HasGenerals { get; }

    /// <summary>
    /// Gets the path to Generals installation within this source.
    /// </summary>
    string GeneralsPath { get; }

    /// <summary>
    /// Gets a value indicating whether Zero Hour is installed in this installation.
    /// </summary>
    bool HasZeroHour { get; }

    /// <summary>
    /// Gets the path to Zero Hour installation within this source.
    /// </summary>
    string ZeroHourPath { get; }

    /// <summary>
    /// Gets the available game clients for this installation.
    /// </summary>
    List<GameClient> AvailableGameClients { get; }
}
