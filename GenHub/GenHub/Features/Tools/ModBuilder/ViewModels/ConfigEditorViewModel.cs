using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for editing ModBuilder configuration (bundle items and packs).
/// </summary>
public partial class ConfigEditorViewModel(
    IConfigurationLoaderService configurationLoaderService,
    INotificationService notificationService,
    ILogger<ConfigEditorViewModel> logger) : ObservableObject
{
    private readonly IConfigurationLoaderService _configurationLoaderService = configurationLoaderService;
    private readonly INotificationService _notificationService = notificationService;
    private readonly ILogger<ConfigEditorViewModel> _logger = logger;

    /// <summary>
    /// Gets or sets the current project.
    /// </summary>
    [ObservableProperty]
    private ModBuilderProject? _currentProject;

    /// <summary>
    /// Gets or sets the build configuration.
    /// </summary>
    [ObservableProperty]
    private BuildConfiguration? _configuration;

    /// <summary>
    /// Gets the list of bundle items.
    /// </summary>
    public ObservableCollection<BundleItemEditorViewModel> BundleItems { get; } = [];

    /// <summary>
    /// Gets the list of bundle packs.
    /// </summary>
    public ObservableCollection<BundlePackConfigViewModel> BundlePacks { get; } = [];

    /// <summary>
    /// Gets or sets the selected bundle item.
    /// </summary>
    [ObservableProperty]
    private BundleItemEditorViewModel? _selectedBundleItem;

    /// <summary>
    /// Gets or sets the selected bundle pack.
    /// </summary>
    [ObservableProperty]
    private BundlePackConfigViewModel? _selectedBundlePack;

    /// <summary>
    /// Gets or sets the active tab index (0 = Items, 1 = Packs).
    /// </summary>
    [ObservableProperty]
    private int _activeTabIndex;

    /// <summary>
    /// Gets or sets a value indicating whether changes have been made.
    /// </summary>
    [ObservableProperty]
    private bool _hasChanges;

    /// <summary>
    /// Initializes the editor with a project.
    /// </summary>
    /// <param name="project">The mod project to initialize with.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync(ModBuilderProject project, CancellationToken cancellationToken = default)
    {
        CurrentProject = project;
        Configuration = project.Configuration;

        if (Configuration == null)
        {
            Configuration = new BuildConfiguration();
            project.Configuration = Configuration;
        }

        await LoadConfigurationAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the configuration into the editor.
    /// </summary>
    private async Task LoadConfigurationAsync(CancellationToken cancellationToken)
    {
        if (Configuration == null)
        {
            return;
        }

        void LoadData()
        {
            BundleItems.Clear();
            BundlePacks.Clear();

            // Load bundle items
            foreach (var item in Configuration.Items)
            {
                var viewModel = new BundleItemEditorViewModel
                {
                    Name = item.Name,
                    NamePrefix = item.NamePrefix,
                    NameSuffix = item.NameSuffix,
                    IsBig = item.IsBig,
                    BigSuffix = item.BigSuffix,
                    SetGameLanguageOnInstall = item.SetGameLanguageOnInstall,
                    FileCount = item.Files.Count,
                };
                BundleItems.Add(viewModel);
            }

            // Load bundle packs
            foreach (var pack in Configuration.Packs)
            {
                var viewModel = new BundlePackConfigViewModel
                {
                    Name = pack.Name,
                    NamePrefix = pack.NamePrefix,
                    NameSuffix = pack.NameSuffix,
                    AllowBuild = pack.AllowBuild,
                    AllowInstall = pack.AllowInstall,
                    SetGameLanguageOnInstall = pack.SetGameLanguageOnInstall,
                };
                foreach (var itemName in pack.ItemNames)
                {
                    viewModel.ItemNames.Add(itemName);
                }

                BundlePacks.Add(viewModel);
            }

            HasChanges = false;
        }

        if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            LoadData();
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(LoadData);
        }
    }

    /// <summary>
    /// Adds a new bundle item.
    /// </summary>
    [RelayCommand]
    private void AddBundleItem()
    {
        var newItem = new BundleItemEditorViewModel
        {
            Name = $"NewBundle{BundleItems.Count + 1}",
            IsBig = true,
        };

        BundleItems.Add(newItem);
        SelectedBundleItem = newItem;
        HasChanges = true;
    }

    /// <summary>
    /// Removes the selected bundle item.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveBundleItem))]
    private void RemoveBundleItem()
    {
        if (SelectedBundleItem == null)
        {
            return;
        }

        BundleItems.Remove(SelectedBundleItem);
        SelectedBundleItem = null;
        HasChanges = true;
    }

    private bool CanRemoveBundleItem() => SelectedBundleItem != null;

    /// <summary>
    /// Adds a new bundle pack.
    /// </summary>
    [RelayCommand]
    private void AddBundlePack()
    {
        var newPack = new BundlePackConfigViewModel
        {
            Name = $"NewPack{BundlePacks.Count + 1}",
            AllowBuild = true,
            AllowInstall = true,
        };

        BundlePacks.Add(newPack);
        SelectedBundlePack = newPack;
        HasChanges = true;
    }

    /// <summary>
    /// Removes the selected bundle pack.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveBundlePack))]
    private void RemoveBundlePack()
    {
        if (SelectedBundlePack == null)
        {
            return;
        }

        BundlePacks.Remove(SelectedBundlePack);
        SelectedBundlePack = null;
        HasChanges = true;
    }

    private bool CanRemoveBundlePack() => SelectedBundlePack != null;

    /// <summary>
    /// Saves the configuration changes.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Configuration == null || CurrentProject == null)
        {
            return;
        }

        try
        {
            // Index existing items by name to preserve files and events
            var existingItems = Configuration.Items.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);

            // Update configuration from view models
            Configuration.Items.Clear();
            foreach (var itemVm in BundleItems)
            {
                existingItems.TryGetValue(itemVm.Name, out var existingItem);

                var item = new BundleItem
                {
                    Name = itemVm.Name,
                    NamePrefix = itemVm.NamePrefix,
                    NameSuffix = itemVm.NameSuffix,
                    IsBig = itemVm.IsBig,
                    BigSuffix = itemVm.BigSuffix,
                    SetGameLanguageOnInstall = itemVm.SetGameLanguageOnInstall,
                    Files = existingItem?.Files != null ? new List<BundleFile>(existingItem.Files) : [],
                    Events = existingItem?.Events != null ? new Dictionary<BundleEventType, BundleEvent>(existingItem.Events) : [],
                };
                Configuration.Items.Add(item);
            }

            Configuration.Packs.Clear();
            foreach (var packVm in BundlePacks)
            {
                var pack = new BundlePack
                {
                    Name = packVm.Name,
                    NamePrefix = packVm.NamePrefix,
                    NameSuffix = packVm.NameSuffix,
                    AllowBuild = packVm.AllowBuild,
                    AllowInstall = packVm.AllowInstall,
                    SetGameLanguageOnInstall = packVm.SetGameLanguageOnInstall,
                    ItemNames = packVm.ItemNames.ToList(),
                };
                Configuration.Packs.Add(pack);
            }

            HasChanges = false;
            _notificationService.ShowSuccess("Configuration Saved", "Configuration changes saved successfully");
            _logger.LogInformation("Configuration saved successfully");

            // Close the dialog after successful save
            if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
            {
                CloseDialog();
            }
            else
            {
                Dispatcher.UIThread.Post(CloseDialog);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration");
            _notificationService.ShowError("Save Failed", ex.Message);
        }
    }

    /// <summary>
    /// Cancels the configuration changes.
    /// </summary>
    [RelayCommand]
    private async Task CancelAsync()
    {
        if (HasChanges)
        {
            // TODO: Show confirmation dialog
            await LoadConfigurationAsync(CancellationToken.None).ConfigureAwait(false);
        }

        // Close the dialog
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            CloseDialog();
        }
        else
        {
            Dispatcher.UIThread.Post(CloseDialog);
        }
    }

    private static void CloseDialog()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            var windows = lifetime.Windows;
            var configDialog = windows.FirstOrDefault(w => w is Views.ConfigEditorDialog);
            configDialog?.Close();
        }
    }

    partial void OnSelectedBundleItemChanged(BundleItemEditorViewModel? value)
    {
        RemoveBundleItemCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedBundlePackChanged(BundlePackConfigViewModel? value)
    {
        RemoveBundlePackCommand.NotifyCanExecuteChanged();
    }
}
