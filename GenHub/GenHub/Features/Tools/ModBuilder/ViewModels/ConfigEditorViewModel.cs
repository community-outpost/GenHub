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

        await LoadConfigurationAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the configuration into the editor.
    /// </summary>
    private async Task LoadConfigurationAsync()
    {
        if (Configuration == null)
        {
            return;
        }

        void LoadData()
        {
            BundleItems.Clear();
            BundlePacks.Clear();

            PopulateBundleItems(Configuration);
            PopulateBundlePacks(Configuration);

            SelectedBundleItem = BundleItems.FirstOrDefault();
            SelectedBundlePack = BundlePacks.FirstOrDefault();

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

    private void PopulateBundleItems(BuildConfiguration configuration)
    {
        foreach (var item in configuration.Items)
        {
            var pattern = item.Files.Count > 0
                ? string.Join("; ", item.Files.Select(f => f.AbsSourceFile))
                : "GameFilesEdited/**/*.*";

            BundleItems.Add(new BundleItemEditorViewModel
            {
                Name = item.Name,
                NamePrefix = item.NamePrefix,
                NameSuffix = item.NameSuffix,
                IsBig = item.IsBig,
                BigSuffix = item.BigSuffix,
                SetGameLanguageOnInstall = item.SetGameLanguageOnInstall,
                FileCount = item.Files.Count,
                SourcePattern = pattern,
            });
        }
    }

    private void PopulateBundlePacks(BuildConfiguration configuration)
    {
        foreach (var pack in configuration.Packs)
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
            Name = $"NewBundleItem{BundleItems.Count + 1}",
            NamePrefix = string.Empty,
            NameSuffix = string.Empty,
            IsBig = false,
            BigSuffix = string.Empty,
            SetGameLanguageOnInstall = string.Empty,
            FileCount = 0,
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
            Name = $"NewBundlePack{BundlePacks.Count + 1}",
            NamePrefix = string.Empty,
            NameSuffix = string.Empty,
            AllowBuild = true,
            AllowInstall = true,
            SetGameLanguageOnInstall = string.Empty,
        };

        foreach (var item in BundleItems.Where(item => !string.IsNullOrEmpty(item.Name)))
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
            var existingItems = Configuration.Items
                .Where(i => !string.IsNullOrEmpty(i.Name))
                .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            Configuration.Items.Clear();
            foreach (var itemVm in BundleItems)
            {
                existingItems.TryGetValue(itemVm.Name, out var existingItem);
                Configuration.Items.Add(new BundleItem
                {
                    Name = itemVm.Name,
                    NamePrefix = itemVm.NamePrefix,
                    NameSuffix = itemVm.NameSuffix,
                    IsBig = itemVm.IsBig,
                    BigSuffix = itemVm.BigSuffix,
                    SetGameLanguageOnInstall = itemVm.SetGameLanguageOnInstall,
                    Files = ParseItemFiles(itemVm, existingItem),
                    Events = existingItem?.Events != null ? new Dictionary<BundleEventType, BundleEvent>(existingItem.Events) : [],
                });
            }

            Configuration.Packs.Clear();
            foreach (var packVm in BundlePacks)
            {
                Configuration.Packs.Add(new BundlePack
                {
                    Name = packVm.Name,
                    NamePrefix = packVm.NamePrefix,
                    NameSuffix = packVm.NameSuffix,
                    AllowBuild = packVm.AllowBuild,
                    AllowInstall = packVm.AllowInstall,
                    SetGameLanguageOnInstall = packVm.SetGameLanguageOnInstall,
                    ItemNames = packVm.ItemNames.ToList(),
                });
            }

            PersistConfigurationToDisk(CurrentProject.ProjectDir);

            HasChanges = false;
            notificationService.ShowSuccess("Configuration Saved", "Configuration changes saved successfully");
            logger.LogInformation("Configuration saved successfully");

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

    private static List<BundleFile> ParseItemFiles(BundleItemEditorViewModel itemVm, BundleItem? existingItem)
    {
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

        return files;
    }

    private void PersistConfigurationToDisk(string? projectDir)
    {
        if (string.IsNullOrEmpty(projectDir) || Configuration == null)
        {
            return;
        }

        var configDir = Path.Combine(projectDir, ModBuilderConstants.ConfigDir);
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

    /// <summary>
    /// Cancels the configuration changes.
    /// </summary>
    [RelayCommand]
    private async Task CancelAsync()
    {
        if (HasChanges)
        {
            // Revert unsaved modifications by reloading current configuration state
            await LoadConfigurationAsync().ConfigureAwait(false);
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
