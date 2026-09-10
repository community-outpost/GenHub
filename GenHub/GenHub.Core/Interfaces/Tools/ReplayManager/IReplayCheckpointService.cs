using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ReplayManager;

namespace GenHub.Core.Interfaces.Tools.ReplayManager;

/// <summary>
/// Service responsible for managing replay checkpoints, minting save states from replays,
/// resuming playback deterministically, and taking over live player control.
/// </summary>
public interface IReplayCheckpointService
{
    /// <summary>
    /// Gets the path to the save directory for the given game version.
    /// </summary>
    /// <param name="gameType">The game version.</param>
    /// <returns>The path to the Save directory.</returns>
    string GetSaveDirectory(GameType gameType);

    /// <summary>
    /// Retrieves all existing checkpoint save files for a given replay.
    /// </summary>
    /// <param name="replay">The replay file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of checkpoint save information records.</returns>
    Task<IReadOnlyList<ReplayCheckpointInfo>> GetCheckpointsForReplayAsync(ReplayFile replay, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mints a new checkpoint save from a replay at the specified target frame.
    /// Launches the game profile headlessly in fast-forward replay mode with -saveatframe and -quitatframe.
    /// </summary>
    /// <param name="replay">The replay file to mint from.</param>
    /// <param name="profile">The game profile to execute the game client.</param>
    /// <param name="targetFrame">The frame number at which the checkpoint save is minted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An operation result containing the minted checkpoint info.</returns>
    Task<ProfileOperationResult<ReplayCheckpointInfo>> MintCheckpointAsync(
        ReplayFile replay,
        GameProfile profile,
        int targetFrame,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes playback of a replay deterministically from an existing checkpoint save.
    /// </summary>
    /// <param name="replay">The original replay file.</param>
    /// <param name="profile">The compatible game profile to launch.</param>
    /// <param name="checkpoint">The checkpoint save to load.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An operation result containing game launch info.</returns>
    Task<ProfileOperationResult<GameLaunchInfo>> ResumeReplayAsync(
        ReplayFile replay,
        GameProfile profile,
        ReplayCheckpointInfo checkpoint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes over live gameplay control of a replay at an existing checkpoint as a specified player slot.
    /// </summary>
    /// <param name="replay">The original replay file.</param>
    /// <param name="profile">The compatible game profile to launch.</param>
    /// <param name="checkpoint">The checkpoint save to load.</param>
    /// <param name="slotIndex">The player slot index to take over control of.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An operation result containing game launch info.</returns>
    Task<ProfileOperationResult<GameLaunchInfo>> TakeoverMatchAsync(
        ReplayFile replay,
        GameProfile profile,
        ReplayCheckpointInfo checkpoint,
        int slotIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels any in-progress checkpoint minting operation.
    /// </summary>
    void CancelActiveMint();

    /// <summary>
    /// Deletes a checkpoint save file.
    /// </summary>
    /// <param name="checkpoint">The checkpoint to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True if deleted successfully; otherwise false.</returns>
    Task<bool> DeleteCheckpointAsync(ReplayCheckpointInfo checkpoint, CancellationToken cancellationToken = default);
}
