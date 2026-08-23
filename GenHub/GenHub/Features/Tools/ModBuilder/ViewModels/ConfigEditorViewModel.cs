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
using System.IO;
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
    /// Gets the list of selectable bundle items for the currently selected bundle pack.
    /// </summary>
    public ObservableCollection<BundleItemSelectionItemViewModel> PackItemSelections { get; } = [];

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
                var pattern = item.Files.Count > 0
                    ? string.Join("; ", item.Files.Select(f => f.AbsSourceFile))
                    : "GameFilesEdited/**/*.*";

                var viewModel = new BundleItemEditorViewModel
                {
                    Name = item.Name,
                    NamePrefix = item.NamePrefix,
                    NameSuffix = item.NameSuffix,
                    IsBig = item.IsBig,
                    BigSuffix = item.BigSuffix,
                    SetGameLanguageOnInstall = item.SetGameLanguageOnInstall,
                    FileCount = item.Files.Count,
                    SourcePattern = pattern,
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

            if (BundleItems.Count > 0)
            {
                SelectedBundleItem = BundleItems[0];
            }

            if (BundlePacks.Count > 0)
            {
                SelectedBundlePack = BundlePacks[0];
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
    /// Sets a predefined source pattern on the selected bundle item.
    /// </summary>
    /// <param name="pattern">The glob pattern to apply.</param>
    [RelayCommand]
    private void SetSourcePattern(string pattern)
    {
        if (SelectedBundleItem != null && !string.IsNullOrEmpty(pattern))
        {
            SelectedBundleItem.SourcePattern = pattern;
            HasChanges = true;
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
            SourcePattern = "GameFilesEdited/**/*.*",
        };

        BundleItems.Add(newItem);
        SelectedBundleItem = newItem;
        HasChanges = true;
        UpdatePackItemSelections();
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

        foreach (var item in BundleItems)
        {
            newPack.ItemNames.Add(item.Name);
        }

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
    private void Save()
    {
        if (Configuration == null || CurrentProject == null)
        {
            return;
        }

        try
        {
            // Index existing items by name safely to prevent duplicate key crashes
            var existingItems = new Dictionary<string, BundleItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in Configuration.Items)
            {
                if (!string.IsNullOrEmpty(item.Name) && !existingItems.ContainsKey(item.Name))
                {
                    existingItems[item.Name] = item;
                }
            }

            // Update configuration from view models
            Configuration.Items.Clear();
            foreach (var itemVm in BundleItems)
            {
                existingItems.TryGetValue(itemVm.Name, out var existingItem);

                var files = new List<BundleFile>();
                if (!string.IsNullOrWhiteSpace(itemVm.SourcePattern))
                {
                    var patterns = itemVm.SourcePattern.Split([';', ','], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in patterns)
                    {
                        var trimmed = p.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            files.Add(new BundleFile { AbsSourceFile = trimmed });
                        }
                    }
                }
                else if (existingItem?.Files != null && existingItem.Files.Count > 0)
                {
                    files.AddRange(existingItem.Files);
                }
                else
                {
                    files.Add(new BundleFile { AbsSourceFile = "GameFilesEdited/**/*.*" });
                }

                var item = new BundleItem
                {
                    Name = itemVm.Name,
                    NamePrefix = itemVm.NamePrefix,
                    NameSuffix = itemVm.NameSuffix,
                    IsBig = itemVm.IsBig,
                    BigSuffix = itemVm.BigSuffix,
                    SetGameLanguageOnInstall = itemVm.SetGameLanguageOnInstall,
                    Files = files,
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

            // Persist configuration files to disk if project directory exists
            if (!string.IsNullOrEmpty(CurrentProject.ProjectDir))
            {
                var configDir = Path.Combine(CurrentProject.ProjectDir, ModBuilderConstants.ConfigDir);
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                var itemsPath = Path.Combine(configDir, ModBuilderConstants.BundleItemsConfigFileName);
                var packsPath = Path.Combine(configDir, ModBuilderConstants.BundlePacksConfigFileName);

                var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                var itemsConfig = new BuildConfiguration { Items = Configuration.Items };
                var packsConfig = new BuildConfiguration { Packs = Configuration.Packs };
                File.WriteAllText(itemsPath, System.Text.Json.JsonSerializer.Serialize(itemsConfig, jsonOptions));
                File.WriteAllText(packsPath, System.Text.Json.JsonSerializer.Serialize(packsConfig, jsonOptions));
            }

            HasChanges = false;
            notificationService.ShowSuccess("Configuration Saved", "Configuration changes saved successfully");
            logger.LogInformation("Configuration saved successfully");

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
            logger.LogError(ex, "Failed to save configuration");
            notificationService.ShowError("Save Failed", $"Failed to save configuration: {ex.Message}");
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
        UpdatePackItemSelections();
    }

    private void UpdatePackItemSelections()
    {
        PackItemSelections.Clear();
        if (SelectedBundlePack == null)
        {
            return;
        }

        foreach (var item in BundleItems)
        {
            var itemName = item.Name;
            var isIncluded = SelectedBundlePack.ItemNames.Contains(itemName, StringComparer.OrdinalIgnoreCase);
            PackItemSelections.Add(new BundleItemSelectionItemViewModel(itemName, isIncluded, selected =>
            {
                if (SelectedBundlePack == null) return;
                HasChanges = true;
                if (selected && !SelectedBundlePack.ItemNames.Contains(itemName, StringComparer.OrdinalIgnoreCase))
                {
                    SelectedBundlePack.ItemNames.Add(itemName);
                }
                else if (!selected && SelectedBundlePack.ItemNames.Contains(itemName, StringComparer.OrdinalIgnoreCase))
                {
                    var match = SelectedBundlePack.ItemNames.FirstOrDefault(n => string.Equals(n, itemName, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        SelectedBundlePack.ItemNames.Remove(match);
                    }
                }
            }));
        }
    }
}
