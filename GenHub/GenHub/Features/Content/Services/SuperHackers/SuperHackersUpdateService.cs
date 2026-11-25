using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.SuperHackers;

/// <summary>
/// Background service for checking SuperHackers updates via GitHub.
/// </summary>
public class SuperHackersUpdateService(
    ILogger<SuperHackersUpdateService> logger,
    IContentManifestPool manifestPool,
    IGitHubApiClient gitHubApiClient,
    IContentVersionComparer versionComparer) : ContentUpdateServiceBase(logger), ISuperHackersUpdateService
{
    /// <inheritdoc />
    protected override string ServiceName => SuperHackersConstants.ServiceName;

    /// <inheritdoc />
    protected override TimeSpan UpdateCheckInterval =>
        TimeSpan.FromHours(SuperHackersConstants.UpdateCheckIntervalHours);

    /// <inheritdoc />
    public override async Task<ContentUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Checking for SuperHackers updates");

        try
        {
            // Get current installed version
            var currentVersion = await GetInstalledVersionAsync(cancellationToken);

            // Get latest version from GitHub
            var latestVersion = await GetLatestVersionFromGitHubAsync(cancellationToken);

            if (string.IsNullOrEmpty(latestVersion))
            {
                return ContentUpdateCheckResult.CreateFailure(
                    "Could not retrieve latest version from GitHub",
                    currentVersion);
            }

            var updateAvailable = IsNewerVersion(latestVersion, currentVersion);

            if (updateAvailable)
            {
                return ContentUpdateCheckResult.CreateUpdateAvailable(
                    latestVersion: latestVersion,
                    currentVersion: currentVersion);
            }

            return ContentUpdateCheckResult.CreateNoUpdateAvailable(
                currentVersion: currentVersion,
                latestVersion: latestVersion);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check for SuperHackers updates");
            return ContentUpdateCheckResult.CreateFailure(ex.Message);
        }
    }

    private bool IsNewerVersion(string latestVersion, string? currentVersion)
    {
        if (string.IsNullOrEmpty(currentVersion))
        {
            return true; // Any version is newer than nothing
        }

        return versionComparer.IsNewer(latestVersion, currentVersion, PublisherTypeConstants.TheSuperHackers);
    }

    private async Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var manifests = await manifestPool.GetAllManifestsAsync(cancellationToken);
            if (!manifests.Success || manifests.Data == null)
            {
                return null;
            }

            var shManifests = manifests.Data
                .Where(m => m.Publisher?.PublisherType?.Equals(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            if (shManifests.Count == 0) return null;

            // Sort by version descending and pick the newest one installed
            // This ensures we check updates against the latest version the user has, avoiding false positives
            // if they keep old versions installed.
            var newest = shManifests
                .OrderByDescending(m => m.Version, versionComparer.GetScheme(PublisherTypeConstants.TheSuperHackers))
                .FirstOrDefault();

            return newest?.Version;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get installed SuperHackers version");
            return null;
        }
    }

    private async Task<string?> GetLatestVersionFromGitHubAsync(CancellationToken cancellationToken)
    {
        try
        {
            var release = await gitHubApiClient.GetLatestReleaseAsync(
                SuperHackersConstants.GeneralsGameCodeOwner,
                SuperHackersConstants.GeneralsGameCodeRepo,
                cancellationToken);

            // Prefer TagName as version
            var version = release?.TagName;

            if (!string.IsNullOrEmpty(version))
            {
                // Remove 'v' prefix if present common in GitHub releases
                version = version.TrimStart('v', 'V');
            }

            logger.LogInformation("Successfully fetched version from GitHub: '{Version}'", version);
            return version;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get latest version from GitHub");
            return null;
        }
    }
}
