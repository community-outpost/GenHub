using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
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
        UpdateBundleItemPackLinks();
        RemoveBundlePackCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedBundleItemChanged(BundleItemEditorViewModel? value)
    {
        UpdateBundleItemPackLinks();
        value?.RecalculateMatches(CurrentProject?.ProjectDir);
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
                selected => OnPackItemSelectedChanged(this, itemName, selected));
            PackItemSelections.Add(selectionVm);
        }
    }

    private void UpdateBundleItemPackLinks()
    {
        if (SelectedBundleItem == null)
        {
            return;
        }

        SelectedBundleItem.PackLinks.Clear();
        foreach (var pack in BundlePacks)
        {
            var isLinked = pack.ItemNames.Contains(SelectedBundleItem.Name, StringComparer.OrdinalIgnoreCase);
            var packLink = new BundlePackLinkItemViewModel(pack.Name, isLinked, (packName, linked) =>
            {
                var targetPack = BundlePacks.FirstOrDefault(p => p.Name.Equals(packName, StringComparison.OrdinalIgnoreCase));
                if (targetPack != null)
                {
                    if (linked)
                    {
                        AddPackItem(targetPack.ItemNames, SelectedBundleItem.Name);
                    }
                    else
                    {
                        RemovePackItem(targetPack.ItemNames, SelectedBundleItem.Name);
                    }

                    HasChanges = true;
                    UpdatePackItemSelections();
                }
            });
            SelectedBundleItem.PackLinks.Add(packLink);
        }
    }

    private static void OnPackItemSelectedChanged(ConfigEditorViewModel vm, string itemName, bool selected)
    {
        if (vm.SelectedBundlePack == null)
        {
            return;
        }

        if (selected)
        {
            AddPackItem(vm.SelectedBundlePack.ItemNames, itemName);
        }
        else
        {
            RemovePackItem(vm.SelectedBundlePack.ItemNames, itemName);
        }

        vm.HasChanges = true;
        vm.UpdateBundleItemPackLinks();
    }

    private static void AddPackItem(IList<string> itemNames, string itemName)
    {
        if (!itemNames.Contains(itemName, StringComparer.OrdinalIgnoreCase))
        {
            itemNames.Add(itemName);
        }
    }

    private static void RemovePackItem(IList<string> itemNames, string itemName)
    {
        var existing = itemNames.FirstOrDefault(n => string.Equals(n, itemName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            itemNames.Remove(existing);
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

            UpdateBundleItemPackLinks();

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

            var itemVm = new BundleItemEditorViewModel
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

            itemVm.RecalculateMatches(CurrentProject?.ProjectDir);
            BundleItems.Add(itemVm);
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
            SelectedBundleItem.ClearPatterns();
            SelectedBundleItem.AddPattern(pattern, CurrentProject?.ProjectDir);
            HasChanges = true;
        }
    }

    /// <summary>
    /// Adds one or more files from the project to the selected bundle item.
    /// </summary>
    /// <param name="owner">Optional owner window.</param>
    [RelayCommand]
    private async Task AddFilesAsync(Window? owner)
    {
        if (SelectedBundleItem == null || CurrentProject == null)
        {
            return;
        }

        var topLevel = GetTopLevelWindow(owner);
        if (topLevel?.StorageProvider == null)
        {
            return;
        }

        var startFolder = await ResolveStartFolderAsync(topLevel).ConfigureAwait(false);
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select files to add to bundle item",
            AllowMultiple = true,
            SuggestedStartLocation = startFolder,
        }).ConfigureAwait(false);

        if (files == null || files.Count == 0)
        {
            return;
        }

        var projectDir = CurrentProject.ProjectDir;
        foreach (var file in files)
        {
            var localPath = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(localPath))
            {
                continue;
            }

            var rel = Path.GetRelativePath(projectDir, localPath).Replace('\\', '/');
            SelectedBundleItem.AddPattern(rel, projectDir);
        }

        HasChanges = true;
    }

    /// <summary>
    /// Adds a directory from the project to the selected bundle item as a recursive glob.
    /// </summary>
    /// <param name="owner">Optional owner window.</param>
    [RelayCommand]
    private async Task AddFolderAsync(Window? owner)
    {
        if (SelectedBundleItem == null || CurrentProject == null)
        {
            return;
        }

        var topLevel = GetTopLevelWindow(owner);
        if (topLevel?.StorageProvider == null)
        {
            return;
        }

        var startFolder = await ResolveStartFolderAsync(topLevel).ConfigureAwait(false);
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select directory to add to bundle item",
            AllowMultiple = false,
            SuggestedStartLocation = startFolder,
        }).ConfigureAwait(false);

        if (folders == null || folders.Count == 0)
        {
            return;
        }

        var projectDir = CurrentProject.ProjectDir;
        var folder = folders[0];
        var localPath = folder.TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath))
        {
            return;
        }

        var rel = Path.GetRelativePath(projectDir, localPath).Trim('/').Replace('\\', '/');
        var glob = $"{rel}/**/*.*";
        SelectedBundleItem.AddPattern(glob, projectDir);
        HasChanges = true;
    }

    /// <summary>
    /// Opens the interactive project tree selector dialog.
    /// </summary>
    /// <param name="owner">Optional owner window.</param>
    [RelayCommand]
    private async Task OpenProjectTreeSelectorAsync(Window? owner)
    {
        if (SelectedBundleItem == null || CurrentProject == null)
        {
            return;
        }

        var pickerVm = new ProjectItemPickerViewModel(CurrentProject.ProjectDir);
        var dialog = new Views.ProjectItemPickerDialog(pickerVm);
        var parentWindow = owner ?? GetActiveWindow();

        var confirmed = parentWindow != null
            ? await dialog.ShowDialog<bool>(parentWindow).ConfigureAwait(false)
            : false;

        if (confirmed && dialog.ResultPatterns.Count > 0)
        {
            foreach (var pattern in dialog.ResultPatterns)
            {
                SelectedBundleItem.AddPattern(pattern, CurrentProject.ProjectDir);
            }

            HasChanges = true;
        }
    }

    /// <summary>
    /// Adds a custom pattern from the input field.
    /// </summary>
    [RelayCommand]
    private void AddCustomPattern()
    {
        if (SelectedBundleItem == null || string.IsNullOrWhiteSpace(SelectedBundleItem.CustomPatternInput))
        {
            return;
        }

        SelectedBundleItem.AddPattern(SelectedBundleItem.CustomPatternInput, CurrentProject?.ProjectDir);
        SelectedBundleItem.CustomPatternInput = string.Empty;
        HasChanges = true;
    }

    /// <summary>
    /// Removes a pattern item from the selected bundle item.
    /// </summary>
    /// <param name="item">The pattern item to remove.</param>
    [RelayCommand]
    private void RemoveSourcePattern(SourcePathItemViewModel? item)
    {
        if (SelectedBundleItem == null || item == null)
        {
            return;
        }

        SelectedBundleItem.RemovePattern(item, CurrentProject?.ProjectDir);
        HasChanges = true;
    }

    /// <summary>
    /// Clears all patterns on the selected bundle item.
    /// </summary>
    [RelayCommand]
    private void ClearSourcePatterns()
    {
        if (SelectedBundleItem == null)
        {
            return;
        }

        SelectedBundleItem.ClearPatterns(CurrentProject?.ProjectDir);
        HasChanges = true;
    }

    private static Window? GetActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            return lifetime.Windows.FirstOrDefault(w => w is Views.ConfigEditorDialog) ?? lifetime.MainWindow;
        }

        return null;
    }

    private static TopLevel? GetTopLevelWindow(Window? owner = null)
    {
        var targetWindow = owner ?? GetActiveWindow();
        return targetWindow != null ? TopLevel.GetTopLevel(targetWindow) : null;
    }

    private async Task<IStorageFolder?> ResolveStartFolderAsync(TopLevel topLevel)
    {
        if (CurrentProject == null)
        {
            return null;
        }

        var gameFilesDir = Path.Combine(CurrentProject.ProjectDir, ModBuilderConstants.GameFilesEditedDir);
        var targetDir = Directory.Exists(gameFilesDir) ? gameFilesDir : CurrentProject.ProjectDir;

        if (Directory.Exists(targetDir))
        {
            return await topLevel.StorageProvider.TryGetFolderFromPathAsync(targetDir).ConfigureAwait(false);
        }

        return null;
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

        newItem.RecalculateMatches(CurrentProject?.ProjectDir);
        BundleItems.Add(newItem);
        SelectedBundleItem = newItem;
        HasChanges = true;
        UpdatePackItemSelections();
        UpdateBundleItemPackLinks();
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
        UpdateBundleItemPackLinks();
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
        UpdateBundleItemPackLinks();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "RelayCommand CanExecute callback")]
    private bool CanRemoveBundlePack() => SelectedBundlePack != null;

    /// <summary>
    /// Saves the configuration changes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken = default)
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

            await PersistConfigurationToDiskAsync(CurrentProject.ProjectDir, cancellationToken).ConfigureAwait(false);

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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save configuration");
            notificationService.ShowError("Save Failed", $"Could not save configuration: {ex.Message}");
        }
    }

    private static List<BundleFile> ParseItemFiles(BundleItemEditorViewModel itemVm, BundleItem? existingItem)
    {
        var files = new List<BundleFile>();
        if (!string.IsNullOrWhiteSpace(itemVm.SourcePattern))
        {
            var patterns = itemVm.SourcePattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var pattern in patterns)
            {
                var matchedFile = existingItem?.Files.FirstOrDefault(f => string.Equals(f.AbsSourceFile, pattern, StringComparison.OrdinalIgnoreCase));
                files.Add(new BundleFile
                {
                    AbsSourceFile = pattern,
                    RelTargetFile = matchedFile?.RelTargetFile ?? string.Empty,
                    AbsSourceParent = matchedFile?.AbsSourceParent ?? string.Empty,
                });
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

    private async Task PersistConfigurationToDiskAsync(string projectDir, CancellationToken cancellationToken)
    {
        if (Configuration == null)
        {
            return;
        }

        var configDir = Path.Combine(projectDir, ModBuilderConstants.LowercaseConfigDir);
        Directory.CreateDirectory(configDir);

        var itemsPath = Path.Combine(configDir, ModBuilderConstants.BundleItemsConfigFileName);
        var packsPath = Path.Combine(configDir, ModBuilderConstants.BundlePacksConfigFileName);

        var itemsDto = new
        {
            BundleItems = Configuration.Items.Select(item => new
            {
                Name = item.Name,
                NamePrefix = string.IsNullOrEmpty(item.NamePrefix) ? null : item.NamePrefix,
                NameSuffix = string.IsNullOrEmpty(item.NameSuffix) ? null : item.NameSuffix,
                IsBig = item.IsBig ? (bool?)true : null,
                BigSuffix = string.IsNullOrEmpty(item.BigSuffix) ? null : item.BigSuffix,
                SetGameLanguageOnInstall = string.IsNullOrEmpty(item.SetGameLanguageOnInstall) ? null : item.SetGameLanguageOnInstall,
                SourceFiles = item.Files.Select(f => f.AbsSourceFile).ToArray(),
            }).ToArray(),
        };

        var packsDto = new
        {
            BundlePacks = Configuration.Packs.Select(pack => new
            {
                Name = pack.Name,
                NamePrefix = string.IsNullOrEmpty(pack.NamePrefix) ? null : pack.NamePrefix,
                NameSuffix = string.IsNullOrEmpty(pack.NameSuffix) ? null : pack.NameSuffix,
                Big = pack.Big == true ? (bool?)true : null,
                OutputFile = string.IsNullOrEmpty(pack.OutputFile) ? null : pack.OutputFile,
                SetGameLanguageOnInstall = string.IsNullOrEmpty(pack.SetGameLanguageOnInstall) ? null : pack.SetGameLanguageOnInstall,
                AllowBuild = pack.AllowBuild ? (bool?)true : null,
                AllowInstall = pack.AllowInstall ? (bool?)true : null,
                Items = pack.ItemNames.ToArray(),
            }).ToArray(),
        };

        var serializerOptions = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        var itemsJson = System.Text.Json.JsonSerializer.Serialize(itemsDto, serializerOptions);
        var packsJson = System.Text.Json.JsonSerializer.Serialize(packsDto, serializerOptions);

        await File.WriteAllTextAsync(itemsPath, itemsJson, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(packsPath, packsJson, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Saved bundle configuration to {ItemsPath} and {PacksPath}", itemsPath, packsPath);
    }

    /// <summary>
    /// Cancels changes and closes the dialog.
    /// </summary>
    [RelayCommand]
    private async Task CancelAsync()
    {
        if (HasChanges && CurrentProject != null)
        {
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
