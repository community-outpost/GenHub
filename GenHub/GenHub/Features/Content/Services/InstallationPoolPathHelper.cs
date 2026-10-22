using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Storage;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Helper to ensure the installation CAS pool path is detected and initialized before storing content.
/// </summary>
internal static class InstallationPoolPathHelper
{
    /// <summary>
    /// Forces installation detection and resets the installation pool path.
    /// </summary>
    /// <param name="installationService">The game installation service.</param>
    /// <param name="installationCasPoolService">The installation CAS pool service.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when content acquisition may continue; otherwise, false.</returns>
    public static async Task<bool> EnsureInstallationPoolPathAsync(
        IGameInstallationService installationService,
        IInstallationCasPoolService installationCasPoolService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Forcing installation detection to ensure correct installation pool path");
            installationService.InvalidateCache();

            var installationsResult = await installationService.GetAllInstallationsAsync(cancellationToken);
            if (!installationsResult.Success || installationsResult.Data == null)
            {
                logger.LogWarning(
                    "Failed to get installations for CAS pool path resolution: {Error}; the primary CAS pool will be used",
                    installationsResult.FirstError);
                return true;
            }

            var installations = installationsResult.Data.ToList();
            return await installationCasPoolService.EnsurePoolPathAsync(installations, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ensure installation pool root path is set");
            return false;
        }
    }
}
