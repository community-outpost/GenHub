using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.GitHub;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GitHub.ViewModels;

/// <summary>
/// View model for repository control.
/// </summary>
public partial class RepositoryControlViewModel : ObservableObject
{
    private readonly ILogger<RepositoryControlViewModel> _logger;

    /// <summary>
    /// Gets the repositories collection.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<GitHubRepository> _repositories = new();

    /// <summary>
    /// Gets or sets the selected repository.
    /// </summary>
    [ObservableProperty]
    private GitHubRepository? _selectedRepository;

    /// <summary>
    /// Gets or sets whether loading is in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Gets or sets whether discovering is in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isDiscovering;

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "Click Discover to find C&C repositories";

    /// <summary>
    /// Initializes a new instance of the <see cref="RepositoryControlViewModel"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public RepositoryControlViewModel(ILogger<RepositoryControlViewModel> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Discovers C&amp;C Generals repositories.
    /// </summary>
    [RelayCommand]
    private void DiscoverRepositories()
    {
        IsDiscovering = true;
        StatusMessage = "Discovering C&C Generals repositories...";
        Repositories.Clear();

        try
        {
            // Hardcoded popular C&C Generals repos
            var cncRepos = new[]
            {
                new GitHubRepository
                {
                    RepoOwner = "TheSuperHackers",
                    RepoName = "GeneralsGameCode",
                    Description = "The community patch released by Community Outpost",
                },
                new GitHubRepository
                {
                    RepoOwner = "undead2146",
                    RepoName = "GenHub",
                    Description = "GenHub - C&C Generals content manager",
                },
            };

            foreach (var repo in cncRepos)
            {
                Repositories.Add(repo);
            }

            // Auto-select the first repository
            if (Repositories.Any() && SelectedRepository == null)
            {
                SelectedRepository = Repositories.First();
            }

            StatusMessage = $"Found {Repositories.Count} repositories";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover repositories");
            StatusMessage = "Discovery failed: " + ex.Message;
        }
        finally
        {
            IsDiscovering = false;
        }
    }

    partial void OnSelectedRepositoryChanged(GitHubRepository? value)
    {
        if (value != null)
        {
            StatusMessage = $"Selected: {value.RepoOwner}/{value.RepoName}";
        }
        else
        {
            StatusMessage = "No repository selected";
        }
    }
}
