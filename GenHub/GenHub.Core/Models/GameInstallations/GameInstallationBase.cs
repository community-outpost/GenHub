using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using System.Collections.Generic;

namespace GenHub.Core.Models.GameInstallations;

/// <summary>
/// Base class for platform-specific game installations with shared state and path management.
/// </summary>
public abstract class GameInstallationBase : IGameInstallation
{
    /// <inheritdoc/>
    public abstract string Id { get; }

    /// <inheritdoc/>
    public abstract GameInstallationType InstallationType { get; }

    /// <inheritdoc/>
    public string InstallationPath { get; protected set; } = string.Empty;

    /// <inheritdoc/>
    public bool HasGenerals { get; protected set; }

    /// <inheritdoc/>
    public string GeneralsPath { get; protected set; } = string.Empty;

    /// <inheritdoc/>
    public bool HasZeroHour { get; protected set; }

    /// <inheritdoc/>
    public string ZeroHourPath { get; protected set; } = string.Empty;

    /// <inheritdoc/>
    public List<GameClient> AvailableGameClients { get; protected set; } = [];

    /// <inheritdoc/>
    public abstract void Fetch();

    /// <inheritdoc/>
    public void SetPaths(string? generalsPath, string? zeroHourPath)
    {
        if (!string.IsNullOrEmpty(generalsPath))
        {
            HasGenerals = true;
            GeneralsPath = generalsPath;
        }

        if (!string.IsNullOrEmpty(zeroHourPath))
        {
            HasZeroHour = true;
            ZeroHourPath = zeroHourPath;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Appends rather than replaces: installations accumulate manifest-loaded clients
    /// and detection results across lifecycle phases, and scanned clients carry an
    /// empty <see cref="GameClient.InstallationId"/> until manifest generation, so
    /// filtering by installation is not possible here.
    /// </remarks>
    public void PopulateGameClients(IEnumerable<GameClient> clients)
    {
        AvailableGameClients.AddRange(clients);
    }
}
