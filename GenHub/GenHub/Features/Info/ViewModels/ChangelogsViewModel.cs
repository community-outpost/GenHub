using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.GitHub;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for displaying application changelogs from GitHub releases.
/// </summary>
/// <param name="gitHubApiClient">The GitHub API client.</param>
/// <param name="logger">The logger.</param>
/// <param name="configurationProvider">Optional configuration provider for cache path resolution.</param>
/// <param name="localizationService">Optional localization service.</param>
public partial class ChangelogsViewModel(
    IGitHubApiClient gitHubApiClient,
    ILogger<ChangelogsViewModel> logger,
    IConfigurationProviderService? configurationProvider = null,
    ILocalizationService? localizationService = null) : ObservableObject
{
    private const string RepositoryOwner = "community-outpost";
    private const string RepositoryName = "GenHub";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isUsingCachedData;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// Gets the collection of changelog release items.
    /// </summary>
    public ObservableCollection<ChangelogItemViewModel> Releases { get; } = [];

    /// <summary>
    /// Loads the changelogs from GitHub or local cache fallback.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task LoadChangelogsAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading)
        {
            return;
        }

        try
        {
            IsLoading = true;
            HasError = false;
            IsUsingCachedData = false;
            ErrorMessage = string.Empty;

            var releases = await gitHubApiClient.GetReleasesAsync(RepositoryOwner, RepositoryName, cancellationToken);
            var releaseList = releases?.ToList();

            if (releaseList != null && releaseList.Count > 0)
            {
                Releases.Clear();
                var sortedReleases = releaseList.OrderByDescending(r => r.PublishedAt).ToList();
                for (var i = 0; i < sortedReleases.Count; i++)
                {
                    var isLatest = i == 0;
                    Releases.Add(new ChangelogItemViewModel(sortedReleases[i], isLatest, OpenReleaseUrl));
                }

                await SaveToCacheAsync(sortedReleases, cancellationToken);
                return;
            }

            // If empty or null, attempt reading from offline cache
            if (await TryLoadFromCacheAsync(cancellationToken))
            {
                return;
            }

            logger.LogWarning("No releases found.");
            HasError = true;
            ErrorMessage = gitHubApiClient.IsRateLimited
                ? (localizationService?.GetString("Info.Changelog.RateLimited") ?? "GitHub API rate limit exceeded. Sign in with GitHub in Settings to increase the limit, or try again later.")
                : (localizationService?.GetString("Info.Changelog.NoReleasesFound") ?? "No release changelogs found.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading changelogs from GitHub API, falling back to cache");
            if (await TryLoadFromCacheAsync(cancellationToken))
            {
                return;
            }

            HasError = true;
            ErrorMessage = localizationService?.GetString("Info.Changelog.LoadError") ?? "An error occurred while loading changelogs.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Opens the release on GitHub in the default browser.
    /// </summary>
    /// <param name="url">The URL to open.</param>
    [RelayCommand]
    public void OpenReleaseUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            logger.LogWarning("Invalid or unsafe URL: {Url}", url);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open release URL: {Url}", url);
        }
    }

    private string? GetCacheFilePath()
    {
        try
        {
            var cacheDir = configurationProvider?.GetCachePath();
            if (!string.IsNullOrWhiteSpace(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
                return Path.Combine(cacheDir, InfoConstants.ChangelogsCacheFileName);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to resolve cache directory for changelogs");
        }

        return null;
    }

    private async Task SaveToCacheAsync(List<GitHubRelease> releases, CancellationToken cancellationToken = default)
    {
        var cachePath = GetCacheFilePath();
        if (string.IsNullOrWhiteSpace(cachePath))
        {
            return;
        }

        var tempPath = Path.Combine(Path.GetDirectoryName(cachePath)!, $"{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fileStream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(fileStream, releases, cancellationToken: cancellationToken);
            }

            File.Move(tempPath, cachePath, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
        catch (Exception ex)
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Ignore cleanup error
                }
            }

            logger.LogDebug(ex, "Failed to write changelogs cache to {Path}", cachePath);
        }
    }

    private async Task<bool> TryLoadFromCacheAsync(CancellationToken cancellationToken = default)
    {
        var cachePath = GetCacheFilePath();
        if (string.IsNullOrWhiteSpace(cachePath) || !File.Exists(cachePath))
        {
            return false;
        }

        try
        {
            using var fileStream = File.OpenRead(cachePath);
            var cachedReleases = await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(fileStream, cancellationToken: cancellationToken);
            if (cachedReleases != null && cachedReleases.Count > 0)
            {
                Releases.Clear();
                var sortedReleases = cachedReleases.OrderByDescending(r => r.PublishedAt).ToList();
                for (var i = 0; i < sortedReleases.Count; i++)
                {
                    var isLatest = i == 0;
                    Releases.Add(new ChangelogItemViewModel(sortedReleases[i], isLatest, OpenReleaseUrl));
                }

                IsUsingCachedData = true;
                HasError = false;
                ErrorMessage = string.Empty;
                return true;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to read changelogs cache from {Path}", cachePath);
        }

        return false;
    }
}
