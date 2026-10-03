using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// Imports and exports .teams team exchange files (ScriptTeams chunk files).
/// </summary>
public interface ITeamExchangeService
{
    /// <summary>
    /// Exports one player's non-default teams to a .teams file.
    /// </summary>
    /// <param name="teamsPath">Full path of the target .teams file.</param>
    /// <param name="teams">Teams to export.</param>
    /// <param name="ownerName">Only teams owned by this player are exported.</param>
    /// <param name="defaultTeamNames">Default team names to skip.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exported team count.</returns>
    Task<OperationResult<int>> ExportAsync(
        string teamsPath,
        IReadOnlyList<MapTeamEntry> teams,
        string ownerName,
        IReadOnlySet<string> defaultTeamNames,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports teams from a .teams file, retargeting ownership.
    /// </summary>
    /// <param name="teamsPath">Full path of the .teams file.</param>
    /// <param name="targetOwner">Owner assigned to every imported team.</param>
    /// <param name="existingNames">Existing team names used to uniquify imports.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The imported teams.</returns>
    Task<OperationResult<IReadOnlyList<MapTeamEntry>>> ImportAsync(
        string teamsPath,
        string targetOwner,
        IReadOnlySet<string> existingNames,
        CancellationToken cancellationToken = default);
}
