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
    /// This ensures the installation is persisted across sessions.
    /// </summary>
    /// <param name="installation">The installation to create manifests for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    private async Task CreateAndRegisterManualInstallationManifestsAsync(
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Use the consolidated service method to ensure consistent manifest generation
            // This handles ID generation, SourcePath metadata, and pool registration.
            await installationService.CreateAndRegisterInstallationManifestsAsync(installation, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error creating GameInstallation manifests for manual installation {InstallationId}",
                installation.Id);
        }
    }
}
