using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Helper methods for manifest generation in GameProfileLauncherViewModel.
/// </summary>
public partial class GameProfileLauncherViewModel
{
    /// <summary>
    /// Creates and registers GameInstallation manifests for a manually selected installation.
    /// Mirrors the logic from GameInstallationService.CreateAndRegisterInstallationManifestsAsync.
    /// </summary>
    /// <param name="installation">The installation to create manifests for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task CreateAndRegisterManualInstallationManifestsAsync(
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Create manifest for Generals if it exists
            if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
            {
                await CreateAndRegisterSingleInstallationManifestAsync(
                    installation,
                    GameType.Generals,
                    installation.GeneralsPath,
                    cancellationToken);
            }

            // Create manifest for Zero Hour if it exists
            if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
            {
                await CreateAndRegisterSingleInstallationManifestAsync(
                    installation,
                    GameType.ZeroHour,
                    installation.ZeroHourPath,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error creating GameInstallation manifests for manual installation {InstallationId}",
                installation.Id);
        }
    }

    /// <summary>
    /// Creates and registers a single GameInstallation manifest.
    /// Mirrors the logic from GameInstallationService.CreateAndRegisterSingleInstallationManifestAsync.
    /// </summary>
    /// <param name="installation">The installation.</param>
    /// <param name="gameType">The game type (Generals or ZeroHour).</param>
    /// <param name="installationPath">The path to the game installation.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task CreateAndRegisterSingleInstallationManifestAsync(
        GameInstallation installation,
        GameType gameType,
        string installationPath,
        CancellationToken cancellationToken)
    {
        try
        {
            // Find a base game client for this game type to determine version
            var baseGameClient = installation.AvailableGameClients
                .FirstOrDefault(c => c.GameType == gameType && !c.IsPublisherClient);

            if (baseGameClient == null)
            {
                logger.LogWarning(
                    "No base game client found for {GameType} in manual installation {InstallationId}, skipping GameInstallation manifest creation",
                    gameType,
                    installation.Id);
                return;
            }

            // Determine version for manifest
            var version = baseGameClient.Version;

            // Validate version format:
            // 1. Must not be null/empty
            // 2. Must not be a placeholder like GameClientConstants.UnknownVersion or "Auto-Updated"
            // 3. Must be numeric (digits and dots only) to satisfy ManifestIdGenerator requirements
            bool isInvalidVersion = string.IsNullOrEmpty(version) ||
                                  version.Equals(GameClientConstants.UnknownVersion, StringComparison.OrdinalIgnoreCase) ||
                                  version.Equals("Auto-Updated", StringComparison.OrdinalIgnoreCase) ||
                                  version.Equals(GameClientConstants.AutoDetectedVersion, StringComparison.OrdinalIgnoreCase) ||
                                  !version.All(c => char.IsDigit(c) || c == '.');

            if (isInvalidVersion)
            {
                version = gameType == GameType.ZeroHour
                    ? ManifestConstants.ZeroHourManifestVersion
                    : ManifestConstants.GeneralsManifestVersion;
                logger.LogInformation(
                    "Using default manifest version '{Version}' for {GameType} in manual installation (Detected version was '{OriginalVersion}')",
                    version,
                    gameType,
                    baseGameClient.Version);
            }

            // Create the GameInstallation manifest
            var manifestBuilder = await manifestGenerationService.CreateGameInstallationManifestAsync(
                installationPath,
                gameType,
                installation.InstallationType,
                version);

            var manifest = manifestBuilder.Build();

            // Register the manifest to the pool
            var addResult = await contentManifestPool.AddManifestAsync(manifest, installationPath, null, cancellationToken);

            if (addResult.Success)
            {
                logger.LogInformation(
                    "Registered GameInstallation manifest {ManifestId} for {GameType} in manual installation {InstallationId}",
                    manifest.Id,
                    gameType,
                    installation.Id);
            }
            else
            {
                logger.LogWarning(
                    "Failed to register GameInstallation manifest for {GameType} in manual installation {InstallationId}: {Errors}",
                    gameType,
                    installation.Id,
                    string.Join(", ", addResult.Errors));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error creating GameInstallation manifest for {GameType} in manual installation {InstallationId}",
                gameType,
                installation.Id);
        }
    }
}
