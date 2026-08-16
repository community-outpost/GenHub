using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Features.Tools.ModBuilder.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for the Project Dashboard view.
/// </summary>
public sealed partial class ProjectDashboardViewModel(
    IProjectConfigService projectConfigService,
    INotificationService notificationService,
    ILogger<ProjectDashboardViewModel> logger) : ObservableObject
{
    private readonly IProjectConfigService _projectConfigService = projectConfigService;
    private readonly INotificationService _notificationService = notificationService;
    private readonly ILogger<ProjectDashboardViewModel> _logger = logger;

    /// <summary>
    /// Gets the collection of recent projects.
    /// </summary>
    public ObservableCollection<RecentProjectInfo> RecentProjects { get; } = [];

    /// <summary>
    /// Gets or sets the search query for filtering projects.
    /// </summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether there are recent projects.
    /// </summary>
    [ObservableProperty]
    private bool _hasRecentProjects;

    /// <summary>
    /// Gets or sets the total number of projects.
    /// </summary>
    [ObservableProperty]
    private int _totalProjects;

    /// <summary>
    /// Gets or sets the total number of builds.
    /// </summary>
    [ObservableProperty]
    private int _totalBuilds;

    /// <summary>
    /// Event raised when a project is selected.
    /// </summary>
    public event EventHandler<string>? ProjectSelected;

    /// <summary>
    /// Event raised when a new project is requested.
    /// </summary>
    public event EventHandler? NewProjectRequested;

    /// <summary>
    /// Initializes the dashboard by loading recent projects.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        try
        {
            await LoadRecentProjectsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize project dashboard");
            _notificationService.ShowError(
                "Dashboard Error",
                "Failed to load recent projects. Please try again.");
        }
    }

    /// <summary>
    /// Loads recent projects from the project configuration service.
    /// </summary>
    private async Task LoadRecentProjectsAsync()
    {
        RecentProjects.Clear();

        // TODO: Implement actual recent projects loading from IProjectConfigService
        // For now, create sample data for UI testing
        var sampleProjects = new[]
        {
            new RecentProjectInfo
            {
                Name = "Zero Hour Enhanced",
                Path = @"C:\Projects\ZeroHourEnhanced\project.json",
                FileCount = 1247,
                BundlePackCount = 8,
                LastBuildTime = DateTime.Now.AddHours(-2),
                Version = "1.5.0",
                Author = "ModTeam"
            },
            new RecentProjectInfo
            {
                Name = "Generals Remastered",
                Path = @"C:\Projects\GeneralsRemastered\project.json",
                FileCount = 892,
                BundlePackCount = 6,
                LastBuildTime = DateTime.Now.AddDays(-1),
                Version = "2.0.0",
                Author = "Community"
            },
            new RecentProjectInfo
            {
                Name = "Rise of the Reds",
                Path = @"C:\Projects\RiseOfTheReds\project.json",
                FileCount = 2341,
                BundlePackCount = 12,
                LastBuildTime = DateTime.Now.AddDays(-3),
                Version = "3.1.0",
                Author = "ROTR Team"
            }
        };

        foreach (var project in sampleProjects)
        {
            RecentProjects.Add(project);
        }

        HasRecentProjects = RecentProjects.Count > 0;
        TotalProjects = RecentProjects.Count;
        TotalBuilds = RecentProjects.Count * 5; // Sample calculation

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Command to create a new project.
    /// </summary>
    [RelayCommand]
    private async Task NewProjectAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                return;
            }

            var mainWindow = desktop.MainWindow;
            if (mainWindow == null)
            {
                return;
            }

            var file = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Create New ModBuilder Project",
                SuggestedFileName = "project.json",
                FileTypeChoices =
                [
                    new FilePickerFileType("ModBuilder Project")
                    {
                        Patterns = ["*.json"]
                    }
                ]
            });

            if (file != null)
            {
                var projectPath = file.Path.LocalPath;
                _logger.LogInformation("Creating new project at: {ProjectPath}", projectPath);

                // Raise event to notify parent that a new project should be created
                NewProjectRequested?.Invoke(this, EventArgs.Empty);

                _notificationService.ShowSuccess(
                    "Project Created",
                    $"New project created at {Path.GetFileName(projectPath)}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create new project");
            _notificationService.ShowError(
                "Project Creation Failed",
                "Failed to create new project. Please try again.");
        }
    }

    /// <summary>
    /// Command to open an existing project.
    /// </summary>
    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                return;
            }

            var mainWindow = desktop.MainWindow;
            if (mainWindow == null)
            {
                return;
            }

            var files = await mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open ModBuilder Project",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("ModBuilder Project")
                    {
                        Patterns = ["*.json"]
                    }
                ]
            });

            if (files.Count > 0)
            {
                var projectPath = files[0].Path.LocalPath;
                _logger.LogInformation("Opening project: {ProjectPath}", projectPath);

                // Raise event to notify parent that a project should be opened
                ProjectSelected?.Invoke(this, projectPath);

                _notificationService.ShowSuccess(
                    "Project Opened",
                    $"Opened project: {Path.GetFileName(projectPath)}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open project");
            _notificationService.ShowError(
                "Project Open Failed",
                "Failed to open project. Please try again.");
        }
    }

    /// <summary>
    /// Command to open a specific recent project.
    /// </summary>
    /// <param name="projectInfo">The project information.</param>
    [RelayCommand]
    private void OpenRecentProject(RecentProjectInfo projectInfo)
    {
        if (projectInfo == null)
        {
            return;
        }

        _logger.LogInformation("Opening recent project: {ProjectName}", projectInfo.Name);
        ProjectSelected?.Invoke(this, projectInfo.Path);
    }
}
