using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Features.Info.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// A specialized ViewModel for the Add Local Content Demo.
/// This bypasses complex service logic and guarantees static mock data is loaded.
/// </summary>
public partial class DemoAddLocalContentViewModel : AddLocalContentViewModel
{
    private readonly INotificationService? _notificationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DemoAddLocalContentViewModel"/> class.
    /// </summary>
    /// <param name="localContentService">Service for handling local content operations.</param>
    /// <param name="notificationService">Optional notification service for demo actions.</param>
    /// <param name="logger">Logger instance.</param>
    public DemoAddLocalContentViewModel(
        ILocalContentService? localContentService,
        INotificationService? notificationService,
        ILogger<AddLocalContentViewModel>? logger = null)
        : base(localContentService ?? new MockLocalContentService(), logger)
    {
        _notificationService = notificationService;

        // Enable demo mode to hide Cancel button and enable demo-specific behavior
        IsDemoMode = true;

        // Initialize with demo data
        InitializeDemoData();

        // Set up demo actions that return demo paths and show notifications
        SetupDemoActions();
    }

    /// <summary>
    /// Initializes the demo with static mock data.
    /// </summary>
    private void InitializeDemoData()
    {
        // Set default values
        ContentName = "Rise of the Reds v1.87";
        SelectedContentType = ContentType.Mod;
        SelectedGameType = GameType.ZeroHour;
        SourcePath = "C:\\Downloads\\RiseOfTheReds_v1.87.zip";

        // CRITICAL: Ensure IsBusy is false to prevent infinite processing spinner
        // This overrides any state left by base constructor or mock services
        IsBusy = false;

        // Build demo file tree structure
        FileTree.Clear();

        // Create a realistic mod structure with better organization
        var modFolder = new FileTreeItem
        {
            Name = "RiseOfTheReds_v1.87",
            IsFile = false,
            FullPath = "C:\\Demo\\RiseOfTheReds_v1.87",
            Children = new ObservableCollection<FileTreeItem>
            {
                // Core Game Files
                new FileTreeItem
                {
                    Name = "Core Files",
                    IsFile = false,
                    FullPath = "C:\\Demo\\RiseOfTheReds_v1.87\\Core",
                    Children = new ObservableCollection<FileTreeItem>
                    {
                         new FileTreeItem { Name = "ROTR_Installer.exe", IsFile = true, FullPath = "C:\\Demo\\RiseOfTheReds_v1.87\\ROTR_Installer.exe" },
                         new FileTreeItem { Name = "README.txt", IsFile = true, FullPath = "C:\\Demo\\RiseOfTheReds_v1.87\\README.txt" },
                         new FileTreeItem { Name = "License.rtf", IsFile = true, FullPath = "C:\\Demo\\RiseOfTheReds_v1.87\\License.rtf" },
                    },
                },

                // Data Folder
                new FileTreeItem
                {
                    Name = "Data",
                    IsFile = false,
                    FullPath = "C:\\Demo\\RiseOfTheReds_v1.87\\Data",
                    Children = new ObservableCollection<FileTreeItem>
                    {
                        new FileTreeItem { Name = "INI", IsFile = false, FullPath = "C:\\Demo\\RiseOfTheReds_v1.87\\Data\\INI" },
                        new FileTreeItem { Name = "Art", IsFile = false, FullPath = "C:\\Demo\\RiseOfTheReds_v1.87\\Data\\Art" },
                        new FileTreeItem { Name = "Audio", IsFile = false, FullPath = "C:\\Demo\\RiseOfTheReds_v1.87\\Data\\Audio" },
                        new FileTreeItem { Name = "Scripts", IsFile = false, FullPath = "C:\\Demo\\RiseOfTheReds_v1.87\\Data\\Scripts" },
                    },
                },

                // Maps Folder (Organized)
                new FileTreeItem
                {
                    Name = "Maps",
                    IsFile = false,
                    FullPath = "C:\\Demo\\RiseOfTheReds_v1.87\\Maps",
                    Children = new ObservableCollection<FileTreeItem>
                    {
                        new FileTreeItem
                        {
                            Name = "Tournament Desert II",
                            IsFile = false,
                            FullPath = "C:\\Demo\\RiseOfTheReds_v1.87\\Maps\\Tournament Desert II",
                            Children = new ObservableCollection<FileTreeItem>
                            {
                                new FileTreeItem { Name = "Tournament Desert II.map", IsFile = true, FullPath = "C:\\Demo\\RiseOfTheReds_v1.87\\Maps\\Tournament Desert II\\Tournament Desert II.map" },
                                new FileTreeItem { Name = "Tournament Desert II.str", IsFile = true, FullPath = "C:\\Demo\\RiseOfTheReds_v1.87\\Maps\\Tournament Desert II\\Tournament Desert II.str" },
                                new FileTreeItem { Name = "Preview.tga", IsFile = true, FullPath = "C:\\Demo\\RiseOfTheReds_v1.87\\Maps\\Tournament Desert II\\Preview.tga" },
                            },
                        },
                        new FileTreeItem
                        {
                            Name = "Alpine Assault",
                            IsFile = false,
                            FullPath = "C:\\Demo\\RiseOfTheReds_v1.87\\Maps\\Alpine Assault",
                            Children = new ObservableCollection<FileTreeItem>
                            {
                                new FileTreeItem { Name = "Alpine Assault.map", IsFile = true, FullPath = "C:\\Demo\\RiseOfTheReds_v1.87\\Maps\\Alpine Assault\\Alpine Assault.map" },
                            },
                        },
                    },
                },

                // Addons/extras
                new FileTreeItem
                {
                    Name = "Optional Addons",
                    IsFile = false,
                    FullPath = "C:\\Demo\\RiseOfTheReds_v1.87\\Addons",
                    Children = new ObservableCollection<FileTreeItem>
                    {
                         new FileTreeItem { Name = "HD_Textures.big", IsFile = true, FullPath = "C:\\Demo\\RiseOfTheReds_v1.87\\Addons\\HD_Textures.big" },
                    },
                },
            },
        };

        FileTree.Add(modFolder);

        // Set status message
        StatusMessage = "Demo content ready. Click buttons to see what they do!";
    }

    /// <summary>
    /// Sets up demo actions that return demo paths and show notifications.
    /// </summary>
    private void SetupDemoActions()
    {
        // Set up BrowseFolderAction to return demo path and show notification
        BrowseFolderAction = async () =>
        {
            _notificationService?.Show(new Core.Models.Notifications.NotificationMessage(
                Core.Models.Enums.NotificationType.Info,
                "Demo - Browse Folder",
                "In the actual dialog, this opens a folder picker to select a mod or map directory.",
                4000));
            await Task.Delay(100);
            return "C:\\Demo\\ExampleMod";
        };

        // Set up BrowseFileAction to return demo paths and show notification
        BrowseFileAction = async () =>
        {
            _notificationService?.Show(new Core.Models.Notifications.NotificationMessage(
                Core.Models.Enums.NotificationType.Info,
                "Demo - Browse Files",
                "In the actual dialog, this opens a file picker to select .zip archives or individual files.",
                4000));
            await Task.Delay(100);
            return new System.Collections.Generic.List<string> { "C:\\Downloads\\ExampleMod.zip" };
        };
    }

    /// <inheritdoc/>
    public override bool ShowLoadingOverlay => false;
}
