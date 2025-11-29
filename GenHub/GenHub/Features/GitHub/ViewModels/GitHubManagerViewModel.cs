using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.GitHub;
using GenHub.Features.GitHub.Factories;
using GenHub.Features.GitHub.ViewModels.Items;
using GenHub.Features.GitHub.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.GitHub.ViewModels;

/// <summary>
/// Main view model for the GitHub manager.
/// </summary>
public partial class GitHubManagerViewModel : ObservableObject
{
    private const int WorkflowsPerPage = 30;
    private static GitHubManagerWindow? _currentWindow;

    private readonly IGitHubApiClient _gitHubApiClient;
    private readonly IGitHubTokenStorage _tokenStorage;
    private readonly GitHubDisplayItemFactory _itemFactory;
    private readonly ILogger<GitHubManagerViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = GitHubConstants.ReadyMessage;

    [ObservableProperty]
    private double _loadingProgress;

    [ObservableProperty]
    private bool _showProgressBar;

    [ObservableProperty]
    private bool _isRepositoryValid;

    [ObservableProperty]
    private ObservableCollection<GitHubDisplayItemViewModel> _items = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchQuery))]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private ContentModeFilterViewModel _contentModeFilterVM;

    /// <summary>
    /// Gets a value indicating whether there is an active search query.
    /// </summary>
    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(SearchQuery);

    // Lazy initialization for child ViewModels to reduce constructor complexity
    private Lazy<GitHubItemsTreeViewModel> _lazyItemsTreeViewModel;
    private Lazy<GitHubDetailsViewModel> _lazyDetailsViewModel;
    private Lazy<InstallationViewModel> _lazyInstallationViewModel;
    private Lazy<RepositoryControlViewModel> _lazyRepositoryControlVM;

    private string? _currentOwner;
    private string? _currentRepository;
    private int _currentWorkflowPage = 1;
    private bool _hasMoreWorkflows = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubManagerViewModel"/> class.
    /// </summary>
    /// <param name="gitHubApiClient">The GitHub API client.</param>
    /// <param name="tokenStorage">The token storage service.</param>
    /// <param name="itemFactory">The item factory.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="logger">The logger.</param>
    public GitHubManagerViewModel(
        IGitHubApiClient gitHubApiClient,
        IGitHubTokenStorage tokenStorage,
        GitHubDisplayItemFactory itemFactory,
        IServiceProvider serviceProvider,
        ILogger<GitHubManagerViewModel> logger)
    {
        _gitHubApiClient = gitHubApiClient ?? throw new ArgumentNullException(nameof(gitHubApiClient));
        _tokenStorage = tokenStorage ?? throw new ArgumentNullException(nameof(tokenStorage));
        _itemFactory = itemFactory ?? throw new ArgumentNullException(nameof(itemFactory));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize lazy ViewModels
        _lazyItemsTreeViewModel = new Lazy<GitHubItemsTreeViewModel>(
            () => _serviceProvider.GetRequiredService<GitHubItemsTreeViewModel>());
        _lazyDetailsViewModel = new Lazy<GitHubDetailsViewModel>(
            () => _serviceProvider.GetRequiredService<GitHubDetailsViewModel>());
        _lazyInstallationViewModel = new Lazy<InstallationViewModel>(
            () => _serviceProvider.GetRequiredService<InstallationViewModel>());
        _lazyRepositoryControlVM = new Lazy<RepositoryControlViewModel>(
            () => _serviceProvider.GetRequiredService<RepositoryControlViewModel>());

        ContentModeFilterVM = new ContentModeFilterViewModel();

        // Wire up the tree view selection to update details and installation
        ItemsTreeViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(GitHubItemsTreeViewModel.SelectedItem))
            {
                var selectedGitHubItem = ItemsTreeViewModel.SelectedItem?.Child;
                DetailsViewModel.SetSelectedItem(selectedGitHubItem);
                InstallationViewModel.SetSelectedItem(selectedGitHubItem);

                // Log for debugging
                if (selectedGitHubItem != null)
                {
                    _logger.LogDebug(
                        "Selected item: {DisplayName}, CanInstall: {CanInstall}",
                        selectedGitHubItem.DisplayName,
                        selectedGitHubItem.CanInstall);
                }
            }
        };

        // Also wire up the SelectedGitHubItemChanged event for direct updates
        ItemsTreeViewModel.SelectedGitHubItemChanged += (s, selectedItem) =>
        {
            DetailsViewModel.SetSelectedItem(selectedItem);
            InstallationViewModel.SetSelectedItem(selectedItem);

            if (selectedItem != null)
            {
                _logger.LogDebug(
                    "Direct selection: {DisplayName}, Type: {Type}",
                    selectedItem.DisplayName,
                    selectedItem.GetType().Name);
            }
        };

        // Wire up repository selection change
        RepositoryControlVM.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(RepositoryControlVM.SelectedRepository))
            {
                LoadItemsForSelectedRepository();
            }
        };
    }

    /// <summary>
    /// Gets the items tree view model, lazily initialized.
    /// </summary>
    public GitHubItemsTreeViewModel ItemsTreeViewModel => _lazyItemsTreeViewModel.Value;

    /// <summary>
    /// Gets the details view model, lazily initialized.
    /// </summary>
    public GitHubDetailsViewModel DetailsViewModel => _lazyDetailsViewModel.Value;

    /// <summary>
    /// Gets the installation view model, lazily initialized.
    /// </summary>
    public InstallationViewModel InstallationViewModel => _lazyInstallationViewModel.Value;

    /// <summary>
    /// Gets the repository control view model, lazily initialized.
    /// </summary>
    public RepositoryControlViewModel RepositoryControlVM => _lazyRepositoryControlVM.Value;

    /// <summary>
    /// Gets the current owner.
    /// </summary>
    public string? CurrentOwner => _currentOwner;

    /// <summary>
    /// Initializes the ViewModel asynchronously.
    /// </summary>
    /// <returns>A task representing the initialization operation.</returns>
    public async Task InitializeAsync()
    {
        try
        {
            Console.WriteLine("[INIT DEBUG] GitHubManagerViewModel.InitializeAsync started");
            _logger.LogInformation("GitHubManagerViewModel.InitializeAsync started");

            // Try to load saved token from secure storage
            if (_tokenStorage.HasToken())
            {
                Console.WriteLine("[INIT DEBUG] Token storage has a token, attempting to load...");
                try
                {
                    var token = await _tokenStorage.LoadTokenAsync();
                    if (token != null && token.Length > 0)
                    {
                        _gitHubApiClient.SetAuthenticationToken(token);
                        Console.WriteLine($"[INIT DEBUG] Token loaded successfully, length: {token.Length}");
                        _logger.LogInformation("GitHub token loaded successfully from secure storage");
                        StatusMessage = "GitHub token loaded from secure storage";
                    }
                    else
                    {
                        Console.WriteLine("[INIT DEBUG] Token was null or empty after loading");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[INIT DEBUG] Failed to load token: {ex.Message}");
                    _logger.LogWarning(ex, "Failed to load GitHub token from secure storage");
                    StatusMessage = "Failed to load saved token";
                }
            }
            else
            {
                Console.WriteLine("[INIT DEBUG] No saved GitHub token found");
                _logger.LogInformation("No saved GitHub token found");
            }

            // Auto-discover repositories after initialization
            if (RepositoryControlVM?.DiscoverRepositoriesCommand?.CanExecute(null) == true)
            {
                RepositoryControlVM.DiscoverRepositoriesCommand.Execute(null);
                await Task.CompletedTask; // Ensure async nature
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-discovery failed during initialization");
        }
    }

    private bool CanLoadRepository => !IsLoading && RepositoryControlVM?.SelectedRepository != null;

    /// <summary>
    /// Opens the GitHub manager window (singleton pattern).
    /// </summary>
    [RelayCommand]
    public static void OpenGitHubManager()
    {
        if (_currentWindow != null)
        {
            _currentWindow.Activate();
            return;
        }

        _currentWindow = new GitHubManagerWindow();
        _currentWindow.Show();
        _currentWindow.Closed += (s, e) => _currentWindow = null;
    }

    /// <summary>
    /// Loads the repository.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLoadRepository))]
    private async Task LoadRepositoryAsync()
    {
        if (!CanLoadRepository) return;

        IsLoading = true;
        ShowProgressBar = true;
        LoadingProgress = 0;
        StatusMessage = "Initializing...";
        await Task.Delay(50);

        try
        {
            Items.Clear();
            ItemsTreeViewModel.SetItems(Items, null, null);
            LoadingProgress = 5; // Cleared

            var repo = RepositoryControlVM.SelectedRepository;
            if (repo == null) return;

            _currentOwner = repo.RepoOwner;
            _currentRepository = repo.RepoName;
            _currentWorkflowPage = 1;
            _hasMoreWorkflows = true;

            // Load releases FIRST and display immediately (non-blocking)
            StatusMessage = $"Loading releases from {repo.RepoOwner}/{repo.RepoName}...";

            var releases = await _gitHubApiClient.GetReleasesAsync(repo.RepoOwner, repo.RepoName);

            int releaseCount = 0;
            if (releases?.Any() == true)
            {
                StatusMessage = $"Processing {releases.Count()} releases...";
                var releaseItems = _itemFactory.CreateFromReleases(releases, repo.RepoOwner, repo.RepoName);
                foreach (var item in releaseItems)
                {
                    Items.Add(item);
                }

                releaseCount = releases.Count();

                // Update tree view with releases immediately
                ItemsTreeViewModel.SetItems(Items, _currentOwner, _currentRepository);
                StatusMessage = $"✅ Loaded {releaseCount} releases. Loading workflows...";
            }

            // Now load workflows in the background
            StatusMessage = $"Loading workflows from {repo.RepoOwner}/{repo.RepoName}...";

            _logger.LogInformation("About to call GetWorkflowRunsForRepositoryAsync for {Owner}/{Repo} page {Page}", repo.RepoOwner, repo.RepoName, _currentWorkflowPage);

            // Use progress to stream workflows one by one
            var workflowProgress = new Progress<GitHubWorkflowRun>(workflow =>
            {
                var workflowItem = _itemFactory.CreateFromWorkflowRuns(new[] { workflow }, repo.RepoOwner, repo.RepoName).First();
                Items.Add(workflowItem);

                // Add incrementally to tree WITHOUT recreating it
                ItemsTreeViewModel.AddWorkflowItem(workflowItem);

                _logger.LogDebug("Streamed workflow {RunNumber} to UI", workflow.RunNumber);
            });

            var workflowResult = await _gitHubApiClient.GetWorkflowRunsForRepositoryAsync(
                repo.RepoOwner,
                repo.RepoName,
                perPage: WorkflowsPerPage,
                page: _currentWorkflowPage,
                progress: workflowProgress);

            _logger.LogInformation("Returned from GetWorkflowRunsForRepositoryAsync with {Count} workflows, HasMore={HasMore}", workflowResult?.WorkflowRuns?.Count() ?? 0, workflowResult?.HasMore ?? false);

            // Use the HasMore flag from the API result instead of comparing counts
            _hasMoreWorkflows = workflowResult?.HasMore ?? false;

            int workflowCount = workflowResult?.WorkflowRuns?.Count() ?? 0;

            // Build status message based on what was actually loaded
            var statusParts = new List<string>();
            if (releaseCount > 0) statusParts.Add($"{releaseCount} releases");
            if (workflowCount > 0) statusParts.Add($"{workflowCount} workflows");

            var itemsDescription = statusParts.Any() ? $" ({string.Join(", ", statusParts)})" : string.Empty;
            StatusMessage = $"✅ Loaded {Items.Count} items from {repo.RepoOwner}/{repo.RepoName}{itemsDescription}";
            IsRepositoryValid = true;

            _logger.LogInformation(
                "Successfully loaded repository {Owner}/{Repo}: {ReleaseCount} releases, {WorkflowCount} workflows",
                _currentOwner,
                _currentRepository,
                releaseCount,
                workflowCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load repository {Owner}/{Name}", _currentOwner, _currentRepository);
            StatusMessage = $"❌ Error loading {_currentOwner}/{_currentRepository}: {ex.Message}";
            IsRepositoryValid = false;
        }
        finally
        {
            IsLoading = false;
            ShowProgressBar = false;
            LoadMoreWorkflowsCommand.NotifyCanExecuteChanged();
        }
    }

    private void LoadItemsForSelectedRepository()
    {
        if (RepositoryControlVM.SelectedRepository != null)
        {
            _ = LoadRepositoryAsync();
        }
    }

    /// <summary>
    /// Loads more workflows for the current repository.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLoadMoreWorkflows))]
    private async Task LoadMoreWorkflowsAsync()
    {
        if (string.IsNullOrEmpty(_currentOwner) || string.IsNullOrEmpty(_currentRepository))
            return;

        IsLoading = true;
        StatusMessage = "Loading more workflows...";

        try
        {
            _currentWorkflowPage++;

            // Use progress to stream workflows one by one
            var workflowProgress = new Progress<GitHubWorkflowRun>(workflow =>
            {
                var workflowItem = _itemFactory.CreateFromWorkflowRuns(new[] { workflow }, _currentOwner, _currentRepository).First();
                Items.Add(workflowItem);

                // Add incrementally to tree without recreating it
                ItemsTreeViewModel.AddWorkflowItem(workflowItem);
            });

            var workflowResult = await _gitHubApiClient.GetWorkflowRunsForRepositoryAsync(
                _currentOwner,
                _currentRepository,
                perPage: WorkflowsPerPage,
                page: _currentWorkflowPage,
                progress: workflowProgress);

            // Use the HasMore flag from the API result instead of comparing counts
            _hasMoreWorkflows = workflowResult?.HasMore ?? false;

            StatusMessage = $"✅ Loaded {workflowResult?.WorkflowRuns?.Count() ?? 0} more workflows (page {_currentWorkflowPage})";
            _logger.LogInformation("Loaded page {Page} with {Count} workflows, hasMore={HasMore}", _currentWorkflowPage, workflowResult?.WorkflowRuns?.Count() ?? 0, _hasMoreWorkflows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load more workflows for {Owner}/{Repo}", _currentOwner, _currentRepository);
            StatusMessage = $"❌ Error loading more workflows: {ex.Message}";
            _currentWorkflowPage--; // Revert page increment on error
        }
        finally
        {
            IsLoading = false;
            LoadMoreWorkflowsCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanLoadMoreWorkflows() => _hasMoreWorkflows && !IsLoading;

    /// <summary>
    /// Applies search filter to displayed items.
    /// </summary>
    [RelayCommand]
    private void ApplySearch()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            // Show all items
            ItemsTreeViewModel.SetItems(Items, _currentOwner, _currentRepository);
            return;
        }

        var query = SearchQuery.Trim().ToLowerInvariant();

        // Filter items based on search query
        var filtered = Items.Where(item =>
        {
            // Search in display name
            if (item.DisplayName.ToLowerInvariant().Contains(query))
                return true;

            // Search in description
            if (item.Description?.ToLowerInvariant().Contains(query) == true)
                return true;

            // For workflow runs, search in PR numbers
            if (item is GitHubWorkflowDisplayItemViewModel workflow)
            {
                if (workflow.WorkflowRun.PullRequestNumbers.Any(pr => pr.ToString().Contains(query)))
                    return true;

                if (workflow.WorkflowRun.HeadBranch.ToLowerInvariant().Contains(query))
                    return true;

                if (workflow.WorkflowRun.Actor.ToLowerInvariant().Contains(query))
                    return true;

                if (workflow.WorkflowRun.HeadSha.ToLowerInvariant().StartsWith(query))
                    return true;
            }

            return false;
        }).ToList();

        ItemsTreeViewModel.SetItems(new ObservableCollection<GitHubDisplayItemViewModel>(filtered), _currentOwner, _currentRepository);
        StatusMessage = $"Found {filtered.Count} items matching '{SearchQuery}'";
    }

    /// <summary>
    /// Clears the search filter.
    /// </summary>
    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
        ApplySearch();
    }

    /// <summary>
    /// Opens the GitHub token dialog.
    /// </summary>
    [RelayCommand]
    private async Task OpenGitHubTokenDialogAsync()
    {
        try
        {
            var tokenDialogViewModel = _serviceProvider.GetRequiredService<GitHubTokenDialogViewModel>();
            tokenDialogViewModel.Reset(); // Reset state before showing

            var dialog = new GitHubTokenDialogView
            {
                DataContext = tokenDialogViewModel,
            };

            // Subscribe to the ShouldClose property to handle dialog closing
            tokenDialogViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(GitHubTokenDialogViewModel.ShouldClose) &&
                    tokenDialogViewModel.ShouldClose)
                {
                    dialog.Close(tokenDialogViewModel.DialogResult);
                }
            };

            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var parentWindow = _currentWindow ?? lifetime?.MainWindow;

            bool? result = false;
            if (parentWindow != null)
            {
                result = await dialog.ShowDialog<bool?>(parentWindow);
            }
            else
            {
                dialog.Show();
            }

            // Handle the result and bring focus back to GitHub Manager
            if (result == true)
            {
                StatusMessage = "GitHub token configured successfully";
                _logger.LogInformation("GitHub token configured successfully");

                // Auto-refresh repositories after token configuration
                if (RepositoryControlVM?.DiscoverRepositoriesCommand?.CanExecute(null) == true)
                {
                    RepositoryControlVM.DiscoverRepositoriesCommand.Execute(null);
                }
            }
            else
            {
                StatusMessage = "GitHub token configuration cancelled";
            }

            // Bring focus back to the GitHub Manager window
            _currentWindow?.Activate();
            _currentWindow?.Focus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open GitHub token dialog");
            StatusMessage = $"Error opening token dialog: {ex.Message}";
        }
    }
}
