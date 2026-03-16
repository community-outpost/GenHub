using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.SuperHackers;

/// <summary>
/// Background service for checking SuperHackers updates via GitHub.
/// </summary>
public class SuperHackersUpdateService(
    ILogger<SuperHackersUpdateService> logger,
    IContentManifestPool manifestPool,
    IHttpClientFactory httpClientFactory) : ContentUpdateServiceBase(logger), ISuperHackersUpdateService
{
    // HttpClient is created per request via factory, so no need for Dispose or cached instance.

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
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check for SuperHackers updates");
            throw; // Base class handles rescheduling/logging
        }
    }

    private static bool IsNewerVersion(string latestVersion, string? currentVersion)
    {
        if (string.IsNullOrEmpty(currentVersion))
        {
            return true; // Any version is newer than nothing
        }

        return VersionComparer.CompareVersions(latestVersion, currentVersion, PublisherTypeConstants.TheSuperHackers) > 0;
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
                .OrderByDescending(m => m.Version, new VersionStringComparer(PublisherTypeConstants.TheSuperHackers))
                .FirstOrDefault();

            return newest?.Version;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get installed SuperHackers version");
            return null;
        }
    }

    private class VersionStringComparer(string publisherType) : IComparer<string?>
    {
        public int Compare(string? x, string? y) => VersionComparer.CompareVersions(x, y, publisherType);
    }

    private async Task<string?> GetLatestVersionFromGitHubAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Construct GitHub API URL
            var url = $"https://api.github.com/repos/{SuperHackersConstants.GeneralsGameCodeOwner}/{SuperHackersConstants.GeneralsGameCodeRepo}/releases/latest";

            using var httpClient = httpClientFactory.CreateClient(PublisherTypeConstants.TheSuperHackers);

            // Allow override via HttpClient configuration if needed, but default to direct API
            if (httpClient.BaseAddress != null && !httpClient.BaseAddress.ToString().Contains("api.github.com"))
            {
               // This handles if client is pre-configured with a base URL
            }
            else
            {
                 // Ensure User-Agent is set globally or here (GitHub requires it)
                 if (httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
                 {
                     httpClient.DefaultRequestHeaders.Add("User-Agent", "GenHub-Agent");
                 }
            }

            var response = await httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                 logger.LogWarning("GitHub API returned {StatusCode}", response.StatusCode);
                 return null;
            }

            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken: cancellationToken);

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

    // Minimal DTO for GitHub Release
    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
