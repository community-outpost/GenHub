using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.AppUpdate.Services;

/// <summary>
/// Service for managing application updates.
/// </summary>
public class AppUpdateService(
    IGitHubApiClient gitHubApiClient,
    IAppVersionService appVersionService,
    IVersionComparator versionComparator,
    ILogger<AppUpdateService> logger) : IAppUpdateService
{
    private readonly IGitHubApiClient _gitHubApiClient = gitHubApiClient ?? throw new ArgumentNullException(nameof(gitHubApiClient));
    private readonly IAppVersionService _appVersionService = appVersionService ?? throw new ArgumentNullException(nameof(appVersionService));
    private readonly IVersionComparator _versionComparator = versionComparator ?? throw new ArgumentNullException(nameof(versionComparator));
    private readonly ILogger<AppUpdateService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Gets the current application version.
    /// </summary>
    /// <returns>The current version string.</returns>
    public string GetCurrentVersion()
    {
        return _appVersionService.GetCurrentVersion();
    }

    /// <summary>
    /// Checks for available updates from the specified repository with a cancellation token.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repositoryName">The repository name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The update check result.</returns>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        string owner,
        string repositoryName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Checking for updates for {Owner}/{Repository}", owner, repositoryName);

            var currentVersion = _appVersionService.GetCurrentVersion();
            var latestRelease = await _gitHubApiClient.GetLatestReleaseAsync(
                owner,
                repositoryName,
                cancellationToken);

            if (latestRelease == null)
            {
                _logger.LogWarning("No releases found for {Owner}/{Repository}", owner, repositoryName);
                return UpdateCheckResult.NoUpdateAvailable(currentVersion);
            }

            var isNewer = _versionComparator.IsNewer(currentVersion, latestRelease.TagName);

            if (isNewer)
            {
                _logger.LogInformation(
                    "Update available: {CurrentVersion} -> {LatestVersion}",
                    currentVersion,
                    latestRelease.TagName);
                var result = UpdateCheckResult.UpdateAvailable(latestRelease, currentVersion);
                return result;
            }

            _logger.LogInformation("No update available. Current version {CurrentVersion} is up to date", currentVersion);
            return UpdateCheckResult.NoUpdateAvailable(currentVersion, latestRelease.TagName, latestRelease.HtmlUrl);
        }
        catch (OperationCanceledException)
        {
            // Allow cancellation to bubble up - don't catch it
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for updates for {Owner}/{Repository}", owner, repositoryName);
            return UpdateCheckResult.Error(ex.Message);
        }
    }
}