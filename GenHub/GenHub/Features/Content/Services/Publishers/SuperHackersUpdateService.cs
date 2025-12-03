using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Background service for monitoring TheSuperHackers GitHub releases.
/// Checks for new releases from the GeneralsGameCode repository.
/// </summary>
public class SuperHackersUpdateService(
    ILogger<SuperHackersUpdateService> logger,
    IGitHubApiClient gitHubClient,
    IContentManifestPool manifestPool,
    IProviderDefinitionLoader providerLoader)
    : ContentUpdateServiceBase(logger)
{
    /// <inheritdoc />
    protected override string ServiceName => SuperHackersConstants.ServiceName;

    /// <inheritdoc />
    protected override TimeSpan UpdateCheckInterval => TimeSpan.FromHours(SuperHackersConstants.UpdateCheckIntervalHours);

    /// <inheritdoc />
    public override async Task<ContentUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Checking for TheSuperHackers GitHub releases");

        try
        {
            // Get provider definition for repository info
            var provider = providerLoader.GetProvider(SuperHackersConstants.PublisherId);
            if (provider == null)
            {
                logger.LogError("Provider definition not found for {ProviderId}", SuperHackersConstants.PublisherId);
                return ContentUpdateCheckResult.CreateFailure(
                    $"Provider definition '{SuperHackersConstants.PublisherId}' not found. Ensure thesuperhackers.provider.json exists.");
            }

            var repositoryOwner = provider.Endpoints.GetEndpoint("githubOwner");
            var repositoryName = provider.Endpoints.GetEndpoint("githubRepo");

            if (string.IsNullOrEmpty(repositoryOwner) || string.IsNullOrEmpty(repositoryName))
            {
                logger.LogError("GitHub repository info not configured in provider definition");
                return ContentUpdateCheckResult.CreateFailure(
                    "GitHub repository information not found in provider configuration");
            }

            logger.LogDebug("Using GitHub repository: {Owner}/{Repo}", repositoryOwner, repositoryName);

            // Get latest release from GitHub
            var latestRelease = await gitHubClient.GetLatestReleaseAsync(
                repositoryOwner,
                repositoryName,
                cancellationToken);

            if (latestRelease == null)
            {
                logger.LogWarning("No releases found for {Owner}/{Repo}", repositoryOwner, repositoryName);
                return ContentUpdateCheckResult.CreateNoUpdateAvailable();
            }

            var latestVersion = ExtractVersionFromTag(latestRelease.TagName);
            logger.LogDebug(
                "Latest GitHub release: {TagName} (extracted version: {Version})",
                latestRelease.TagName,
                latestVersion);

            // Check installed versions for both Generals and Zero Hour
            var currentVersionGenerals = await GetInstalledVersionAsync(SuperHackersConstants.GeneralsSuffix, cancellationToken);
            var currentVersionZeroHour = await GetInstalledVersionAsync(SuperHackersConstants.ZeroHourSuffix, cancellationToken);

            // Use the higher of the two installed versions
            var currentVersion = string.IsNullOrEmpty(currentVersionGenerals)
                ? currentVersionZeroHour
                : (string.IsNullOrEmpty(currentVersionZeroHour)
                    ? currentVersionGenerals
                    : string.Compare(currentVersionGenerals, currentVersionZeroHour, StringComparison.Ordinal) > 0
                        ? currentVersionGenerals
                        : currentVersionZeroHour);

            logger.LogDebug(
                "Installed versions - Generals: {GeneralsVersion}, ZeroHour: {ZeroHourVersion}, Using: {CurrentVersion}",
                currentVersionGenerals ?? "none",
                currentVersionZeroHour ?? "none",
                currentVersion ?? "none");

            // Compare versions
            bool updateAvailable = CompareVersions(latestVersion, currentVersion);

            if (updateAvailable)
            {
                return ContentUpdateCheckResult.CreateUpdateAvailable(
                    latestVersion,
                    currentVersion,
                    latestRelease.PublishedAt?.DateTime,
                    latestRelease.HtmlUrl,
                    latestRelease.Body);
            }

            return ContentUpdateCheckResult.CreateNoUpdateAvailable(currentVersion, latestVersion);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check for TheSuperHackers updates");
            return ContentUpdateCheckResult.CreateFailure(ex.Message);
        }
    }

    /// <summary>
    /// Extracts version number from GitHub release tag.
    /// Handles formats like "v1.2.3", "1.2.3", "release-1.2.3", etc.
    /// </summary>
    /// <param name="tagName">The GitHub release tag.</param>
    /// <returns>The extracted version string.</returns>
    private static string ExtractVersionFromTag(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            return "0";

        // Remove common prefixes
        var version = tagName
            .Replace("v", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("release-", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("release_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return version;
    }

    /// <summary>
    /// Compares two version strings to determine if an update is available.
    /// </summary>
    /// <param name="latestVersion">The latest available version.</param>
    /// <param name="currentVersion">The currently installed version.</param>
    /// <returns>True if the latest version is newer than the current version.</returns>
    private static bool CompareVersions(string? latestVersion, string? currentVersion)
    {
        if (string.IsNullOrEmpty(latestVersion))
            return false;

        if (string.IsNullOrEmpty(currentVersion))
            return true;

        // Simple string comparison for now
        // Could be enhanced with semantic version parsing if needed
        return string.Compare(latestVersion, currentVersion, StringComparison.Ordinal) > 0;
    }

    /// <summary>
    /// Gets the installed version for a specific game variant.
    /// </summary>
    /// <param name="gameVariant">The game variant (generals or zerohour).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The installed version or null if not found.</returns>
    private async Task<string?> GetInstalledVersionAsync(string gameVariant, CancellationToken cancellationToken)
    {
        try
        {
            // Query manifest pool for TheSuperHackers game client manifests
            var manifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);

            if (!manifestsResult.Success || manifestsResult.Data == null)
            {
                return null;
            }

            // Find manifest for the specific game variant
            var manifest = manifestsResult.Data.FirstOrDefault(m =>
                m.Publisher?.PublisherType?.Equals(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) == true &&
                m.ContentType == ContentType.GameClient &&
                m.Id.Value.Contains(gameVariant, StringComparison.OrdinalIgnoreCase));

            return manifest?.Version;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get installed version for {GameVariant}", gameVariant);
            return null;
        }
    }
}
