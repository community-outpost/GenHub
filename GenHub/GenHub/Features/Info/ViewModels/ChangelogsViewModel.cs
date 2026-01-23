using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.GitHub;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for displaying application changelogs from GitHub releases.
/// </summary>
/// <param name="gitHubApiClient">The GitHub API client.</param>
/// <param name="logger">The logger.</param>
public partial class ChangelogsViewModel(IGitHubApiClient gitHubApiClient, ILogger<ChangelogsViewModel> logger) : ObservableObject
{
    private const string RepositoryOwner = "community-outpost";
    private const string RepositoryName = "GenHub";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// Gets the collection of GitHub releases.
    /// </summary>
    public ObservableCollection<GitHubRelease> Releases { get; } = [];

    /// <summary>
    /// Loads the changelogs from GitHub.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task LoadChangelogsAsync()
    {
        if (IsLoading)
        {
            return;
        }

        try
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = string.Empty;
            Releases.Clear();

            var releases = await gitHubApiClient.GetReleasesAsync(RepositoryOwner, RepositoryName);

            if (releases != null)
            {
                foreach (var release in releases.OrderByDescending(r => r.PublishedAt))
                {
                    Releases.Add(release);
                }
            }

            if (Releases.Count == 0)
            {
                logger.LogWarning("No releases found.");
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = "An error occurred while loading changelogs.";
            logger.LogError(ex, "Error loading changelogs");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Opens the release on GitHub.
    /// </summary>
    /// <param name="url">The URL to open.</param>
    [RelayCommand]
    private void OpenReleaseUrl(string? url)
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
}
