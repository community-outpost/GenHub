using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.GameProfiles;

/// <summary>
/// Service for creating game profiles for game clients.
/// Centralizes profile creation logic for both scan-for-games and content downloads.
/// </summary>
public interface IGameClientProfileService
{
    /// <summary>
    /// Creates a profile for a game client within an installation.
    /// For multi-variant content (GeneralsOnline, SuperHackers), use CreateProfilesForGameClientAsync instead.
    /// </summary>
    /// <param name="installation">The game installation.</param>
    /// <param name="gameClient">The game client to create a profile for.</param>
    /// <param name="iconPath">The optional path to the profile icon.</param>
    /// <param name="coverPath">The optional path to the profile cover image.</param>
    /// <param name="themeColor">The optional theme color for the profile.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result containing the created profile or error information.</returns>
    Task<ProfileOperationResult<GameProfile>> CreateProfileForGameClientAsync(
        GameInstallation installation,
        GameClient gameClient,
        string? iconPath = null,
        string? coverPath = null,
        string? themeColor = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates profiles for a game client within an installation.
    /// Returns ALL created profiles for multi-variant content (GeneralsOnline 30Hz/60Hz, SuperHackers Generals/ZeroHour).
    /// For single-variant content, returns a list with one profile.
    /// </summary>
    /// <param name="installation">The game installation.</param>
    /// <param name="gameClient">The game client.</param>
    /// <param name="iconPath">Optional path to the profile icon.</param>
    /// <param name="coverPath">Optional path to the profile cover.</param>
    /// <param name="themeColor">Optional theme color for the profile.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of results containing created profiles or error information.</returns>
    Task<System.Collections.Generic.List<ProfileOperationResult<GameProfile>>> CreateProfilesForGameClientAsync(
        GameInstallation installation,
        GameClient gameClient,
        string? iconPath = null,
        string? coverPath = null,
        string? themeColor = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a profile from a downloaded content manifest.
    /// Used for auto-profile creation after content acquisition.
    /// </summary>
    /// <param name="manifest">The downloaded content manifest.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the created profile or error information.</returns>
    Task<ProfileOperationResult<GameProfile>> CreateProfileFromManifestAsync(
        ContentManifest manifest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a profile already exists for a given game client ID.
    /// </summary>
    /// <param name="gameClientId">The game client ID to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a profile exists, false otherwise.</returns>
    Task<bool> ProfileExistsForGameClientAsync(
        string gameClientId,
        CancellationToken cancellationToken = default);
}
