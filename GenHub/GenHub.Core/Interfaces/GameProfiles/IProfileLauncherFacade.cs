using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Workspace;

namespace GenHub.Core.Interfaces.GameProfiles;

/// <summary>
/// Provides a facade for game profile launching operations, coordinating workspace preparation,
/// process management, and launch validation.
/// </summary>
public interface IProfileLauncherFacade
{
    /// <summary>
    /// Prepares and launches a game profile with full workspace setup.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to launch.</param>
    /// <param name="skipUserDataCleanup">Whether to skip cleanup of user data files (maps, etc.) from other profiles.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result containing launch information and status.</returns>
    Task<ProfileOperationResult<GameLaunchInfo>> LaunchProfileAsync(string profileId, bool skipUserDataCleanup = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepares and launches a game profile with full workspace setup and transient command line arguments.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to launch.</param>
    /// <param name="skipUserDataCleanup">Whether to skip cleanup of user data files (maps, etc.) from other profiles.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <param name="additionalArguments">Optional transient command line arguments to merge with profile launch options.</param>
    /// <returns>An operation result containing launch information and status.</returns>
    Task<ProfileOperationResult<GameLaunchInfo>> LaunchProfileAsync(
        string profileId,
        bool skipUserDataCleanup,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? additionalArguments);

    /// <summary>
    /// Validates that a profile can be launched successfully.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to validate.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result indicating validation success or failure with details.</returns>
    Task<ProfileOperationResult<bool>> ValidateLaunchAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current launch status and process information for a profile.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result containing current launch status.</returns>
    Task<ProfileOperationResult<GameProcessInfo>> GetLaunchStatusAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops a running game process for the specified profile.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result indicating success or failure of the stop operation.</returns>
    Task<ProfileOperationResult<bool>> StopProfileAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepares the workspace for a profile without launching.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result containing workspace preparation status.</returns>
    Task<ProfileOperationResult<WorkspaceInfo>> PrepareWorkspaceAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a game profile.
    /// </summary>
    /// <param name="profileId">The profile ID to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success.</returns>
    Task<ProfileOperationResult<bool>> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default);
}
