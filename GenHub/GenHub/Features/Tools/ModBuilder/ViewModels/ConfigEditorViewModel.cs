using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for editing ModBuilder configuration (bundle items and packs).
/// </summary>
public partial class ConfigEditorViewModel(
    IConfigurationLoaderService configurationLoaderService,
    INotificationService notificationService,
    ILogger<ConfigEditorViewModel> logger) : ObservableObject
{
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
    /// Handles changes when the selected bundle pack changes.
    /// </summary>
    /// <param name="value">The new selected pack.</param>
    partial void OnSelectedBundlePackChanged(BundlePackConfigViewModel? value)
    {
        UpdatePackItemSelections();
        RemoveBundlePackCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedBundleItemChanged(BundleItemEditorViewModel? value)
    {
        RemoveBundleItemCommand.NotifyCanExecuteChanged();
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
            var isSelected = SelectedBundlePack.ItemNames.Contains(itemName, StringComparer.OrdinalIgnoreCase);
            var selectionVm = new BundleItemSelectionItemViewModel(
                itemName,
                isSelected,
                selected =>
                {
                    if (SelectedBundlePack != null)
                    {
                        if (selected)
                        {
                            if (!SelectedBundlePack.ItemNames.Contains(itemName, StringComparer.OrdinalIgnoreCase))
                            {
                                SelectedBundlePack.ItemNames.Add(itemName);
                            }
                        }
                        else
                        {
                            var existing = SelectedBundlePack.ItemNames.FirstOrDefault(n => string.Equals(n, itemName, StringComparison.OrdinalIgnoreCase));
                            if (existing != null)
                            {
                                SelectedBundlePack.ItemNames.Remove(existing);
                            }
                        }

                        HasChanges = true;
                    }
                });
            PackItemSelections.Add(selectionVm);
        }
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
                Big = pack.IsBigPack,
                OutputFile = pack.OutputFile,
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
        UpdatePackItemSelections();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "RelayCommand CanExecute callback")]
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
            Big = false,
            OutputFile = null,
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "RelayCommand CanExecute callback")]
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
                    Big = packVm.Big,
                    OutputFile = packVm.OutputFile,
                    SetGameLanguageOnInstall = packVm.SetGameLanguageOnInstall,
                    ItemNames = packVm.ItemNames.ToList(),
                });
            }

            await PersistConfigurationToDiskAsync(CurrentProject.ProjectDir).ConfigureAwait(false);

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
        var existingFileMap = existingItem?.Files?
            .Where(f => !string.IsNullOrEmpty(f.AbsSourceFile))
            .GroupBy(f => f.AbsSourceFile, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(itemVm.SourcePattern))
        {
            var patterns = itemVm.SourcePattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var p in patterns)
            {
                if (!string.IsNullOrEmpty(p))
                {
                    if (existingFileMap != null && existingFileMap.TryGetValue(p, out var matched))
                    {
                        files.Add(matched);
                    }
                    else
                    {
                        files.Add(new BundleFile { AbsSourceFile = p });
                    }
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

    private async Task PersistConfigurationToDiskAsync(string? projectDir)
    {
        if (string.IsNullOrEmpty(projectDir) || Configuration == null)
        {
            return;
        }

        var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };

        // 1. Determine target paths, prioritizing files from which configurations were actually loaded
        string? packsPath = Configuration.LoadedConfigFiles.FirstOrDefault(f =>
            string.Equals(Path.GetFileName(f), ModBuilderConstants.BundlePacksConfigFileName, StringComparison.OrdinalIgnoreCase));
        string? itemsPath = Configuration.LoadedConfigFiles.FirstOrDefault(f =>
            string.Equals(Path.GetFileName(f), ModBuilderConstants.BundleItemsConfigFileName, StringComparison.OrdinalIgnoreCase));

        // 2. If not loaded previously, check CurrentProject.BundleConfigs
        if (CurrentProject?.BundleConfigs != null)
        {
            foreach (var cfgRel in CurrentProject.BundleConfigs)
            {
                var candidate = Path.IsPathRooted(cfgRel) ? cfgRel : Path.Combine(projectDir, cfgRel);
                var fileName = Path.GetFileName(candidate);
                if (packsPath == null && string.Equals(fileName, ModBuilderConstants.BundlePacksConfigFileName, StringComparison.OrdinalIgnoreCase))
                {
                    packsPath = candidate;
                }
                else if (itemsPath == null && string.Equals(fileName, ModBuilderConstants.BundleItemsConfigFileName, StringComparison.OrdinalIgnoreCase))
                {
                    itemsPath = candidate;
                }
            }
        }

        // 3. Fallback to candidate directories based on project directories definition or existing folders
        var configDirName = CurrentProject?.Directories?.Configs;
        string configDir;
        if (!string.IsNullOrWhiteSpace(configDirName) && Directory.Exists(Path.Combine(projectDir, configDirName)))
        {
            configDir = Path.Combine(projectDir, configDirName);
        }
        else if (Directory.Exists(Path.Combine(projectDir, "config")))
        {
            configDir = Path.Combine(projectDir, "config");
        }
        else if (Directory.Exists(Path.Combine(projectDir, "Configs")))
        {
            configDir = Path.Combine(projectDir, "Configs");
        }
        else
        {
            configDir = Path.Combine(projectDir, !string.IsNullOrWhiteSpace(configDirName) ? configDirName : ModBuilderConstants.ConfigDir);
            Directory.CreateDirectory(configDir);
        }

        packsPath ??= Path.Combine(configDir, ModBuilderConstants.BundlePacksConfigFileName);
        itemsPath ??= Path.Combine(configDir, ModBuilderConstants.BundleItemsConfigFileName);

        var packsDir = Path.GetDirectoryName(packsPath);
        if (!string.IsNullOrEmpty(packsDir) && !Directory.Exists(packsDir))
        {
            Directory.CreateDirectory(packsDir);
        }

        var itemsDir = Path.GetDirectoryName(itemsPath);
        if (!string.IsNullOrEmpty(itemsDir) && !Directory.Exists(itemsDir))
        {
            Directory.CreateDirectory(itemsDir);
        }

        // Save bundle packs
        var existingPacksText = File.Exists(packsPath) ? await File.ReadAllTextAsync(packsPath).ConfigureAwait(false) : null;
        if (existingPacksText != null && existingPacksText.Contains("\"BundlePacks\"", StringComparison.OrdinalIgnoreCase))
        {
            var simplifiedPacks = Configuration.Packs.Select(p => new
            {
                p.Name,
                Items = p.ItemNames,
                p.OutputFile,
                p.Big,
                p.AllowBuild,
                p.AllowInstall,
            }).ToList();
            var packsData = new Dictionary<string, object>
            {
                ["BundlePacks"] = simplifiedPacks,
            };
            await File.WriteAllTextAsync(packsPath, System.Text.Json.JsonSerializer.Serialize(packsData, jsonOptions)).ConfigureAwait(false);
        }
        else
        {
            var packsConfig = new BuildConfiguration { Packs = Configuration.Packs };
            await File.WriteAllTextAsync(packsPath, System.Text.Json.JsonSerializer.Serialize(packsConfig, jsonOptions)).ConfigureAwait(false);
        }

        // Save bundle items
        var existingItemsText = File.Exists(itemsPath) ? await File.ReadAllTextAsync(itemsPath).ConfigureAwait(false) : null;
        if (existingItemsText != null && existingItemsText.Contains("\"BundleItems\"", StringComparison.OrdinalIgnoreCase))
        {
            var simplifiedItems = Configuration.Items.Select(i => new
            {
                i.Name,
                Files = i.Files.Select(f => f.AbsSourceFile).ToList(),
                Big = i.IsBig,
            }).ToList();
            var itemsData = new Dictionary<string, object>
            {
                ["BundleItems"] = simplifiedItems,
            };
            await File.WriteAllTextAsync(itemsPath, System.Text.Json.JsonSerializer.Serialize(itemsData, jsonOptions)).ConfigureAwait(false);
        }
        else
        {
            var itemsConfig = new BuildConfiguration { Items = Configuration.Items };
            await File.WriteAllTextAsync(itemsPath, System.Text.Json.JsonSerializer.Serialize(itemsConfig, jsonOptions)).ConfigureAwait(false);
        }

        // Keep alternate directory in sync if both config and Configs exist
        var altDirName = configDir.EndsWith("config", StringComparison.OrdinalIgnoreCase) ? "Configs" : "config";
        var altDir = Path.Combine(projectDir, altDirName);
        if (Directory.Exists(altDir))
        {
            var altPacks = Path.Combine(altDir, ModBuilderConstants.BundlePacksConfigFileName);
            if (File.Exists(altPacks) && !string.Equals(altPacks, packsPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(packsPath, altPacks, true);
            }
        }
    }

    /// <summary>
    /// Cancels the configuration changes.
    /// </summary>
    [RelayCommand]
    private async Task CancelAsync()
    {
        if (HasChanges && !string.IsNullOrEmpty(CurrentProject?.ProjectDir))
        {
            // Revert unsaved modifications by reloading current configuration state from disk
            try
            {
                var loaded = await configurationLoaderService.LoadProjectConfigurationAsync(CurrentProject.ProjectDir).ConfigureAwait(false);
                if (loaded != null)
                {
                    Configuration = loaded;
                    CurrentProject.Configuration = loaded;
                    await LoadConfigurationAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to reload configuration on cancel");
            }
        }

        HasChanges = false;
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
}
