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
                selected => OnPackItemSelectedChanged(this, itemName, selected));
            PackItemSelections.Add(selectionVm);
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

    private async Task PersistConfigurationToDiskAsync(string? projectDir, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(projectDir) || Configuration == null)
        {
            return;
        }

        var jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        var (packsPath, itemsPath, configDir) = ResolveConfigPaths(projectDir, Configuration, CurrentProject);

        EnsureDirectoryExistsForFile(packsPath);
        EnsureDirectoryExistsForFile(itemsPath);

        await SaveBundlePacksAsync(packsPath, jsonOptions, cancellationToken).ConfigureAwait(false);
        await SaveBundleItemsAsync(itemsPath, jsonOptions, cancellationToken).ConfigureAwait(false);

        SyncAlternateConfigDirectory(projectDir, configDir, packsPath, itemsPath);
    }

    private static (string PacksPath, string ItemsPath, string ConfigDir) ResolveConfigPaths(
        string projectDir,
        BuildConfiguration? configuration,
        ModBuilderProject? currentProject)
    {
        string? packsPath = configuration?.LoadedConfigFiles.FirstOrDefault(f =>
            string.Equals(Path.GetFileName(f), ModBuilderConstants.BundlePacksConfigFileName, StringComparison.OrdinalIgnoreCase));
        string? itemsPath = configuration?.LoadedConfigFiles.FirstOrDefault(f =>
            string.Equals(Path.GetFileName(f), ModBuilderConstants.BundleItemsConfigFileName, StringComparison.OrdinalIgnoreCase));

        if (currentProject?.BundleConfigs != null)
        {
            (packsPath, itemsPath) = ResolveFromBundleConfigs(currentProject, projectDir, packsPath, itemsPath);
        }

        var configDir = DetermineConfigDirectory(currentProject, projectDir);
        packsPath ??= Path.Combine(configDir, ModBuilderConstants.BundlePacksConfigFileName);
        itemsPath ??= Path.Combine(configDir, ModBuilderConstants.BundleItemsConfigFileName);

        return (packsPath, itemsPath, configDir);
    }

    private static (string? PacksPath, string? ItemsPath) ResolveFromBundleConfigs(ModBuilderProject? currentProject, string projectDir, string? packsPath, string? itemsPath)
    {
        if (currentProject?.BundleConfigs == null)
        {
            return (packsPath, itemsPath);
        }

        foreach (var cfgRel in currentProject.BundleConfigs)
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

        return (packsPath, itemsPath);
    }

    private static string DetermineConfigDirectory(ModBuilderProject? currentProject, string projectDir)
    {
        var configDirName = currentProject?.Directories?.Configs;
        if (!string.IsNullOrWhiteSpace(configDirName) && Directory.Exists(Path.Combine(projectDir, configDirName)))
        {
            return Path.Combine(projectDir, configDirName);
        }

        if (Directory.Exists(Path.Combine(projectDir, ModBuilderConstants.LowercaseConfigDir)))
        {
            return Path.Combine(projectDir, ModBuilderConstants.LowercaseConfigDir);
        }

        if (Directory.Exists(Path.Combine(projectDir, ModBuilderConstants.ConfigDir)))
        {
            return Path.Combine(projectDir, ModBuilderConstants.ConfigDir);
        }

        var fallbackDir = Path.Combine(projectDir, !string.IsNullOrWhiteSpace(configDirName) ? configDirName : ModBuilderConstants.ConfigDir);
        Directory.CreateDirectory(fallbackDir);
        return fallbackDir;
    }

    private static void EnsureDirectoryExistsForFile(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static async Task AtomicWriteFileAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempFile = Path.Combine(dir ?? string.Empty, $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }

            File.Move(tempFile, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }
    }

    private async Task SaveBundlePacksAsync(string packsPath, System.Text.Json.JsonSerializerOptions jsonOptions, CancellationToken cancellationToken = default)
    {
        if (Configuration == null)
        {
            return;
        }

        var existingPacksText = File.Exists(packsPath) ? await File.ReadAllTextAsync(packsPath, cancellationToken).ConfigureAwait(false) : null;
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
            await AtomicWriteFileAsync(packsPath, System.Text.Json.JsonSerializer.Serialize(packsData, jsonOptions), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var packsConfig = new BuildConfiguration { Packs = Configuration.Packs };
            await AtomicWriteFileAsync(packsPath, System.Text.Json.JsonSerializer.Serialize(packsConfig, jsonOptions), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SaveBundleItemsAsync(string itemsPath, System.Text.Json.JsonSerializerOptions jsonOptions, CancellationToken cancellationToken = default)
    {
        if (Configuration == null)
        {
            return;
        }

        var existingItemsText = File.Exists(itemsPath) ? await File.ReadAllTextAsync(itemsPath, cancellationToken).ConfigureAwait(false) : null;
        if (existingItemsText != null && existingItemsText.Contains("\"BundleItems\"", StringComparison.OrdinalIgnoreCase))
        {
            var existingMap = new Dictionary<string, SimplifiedBundleItem>(StringComparer.OrdinalIgnoreCase);
            List<SimplifiedBundleItem>? existingList = null;
            try
            {
                var existingSimplified = System.Text.Json.JsonSerializer.Deserialize<SimplifiedConfigRoot>(existingItemsText);
                if (existingSimplified?.BundleItems != null)
                {
                    existingList = existingSimplified.BundleItems;
                    foreach (var item in existingSimplified.BundleItems.Where(i => !string.IsNullOrWhiteSpace(i.Name)))
                    {
                        existingMap[item.Name!] = item;
                    }
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                logger.LogWarning(ex, "Could not parse existing ModBundleItems.json at {Path} for preserving custom fields", itemsPath);
            }

            var canFallbackByIndex = existingList != null && existingList.Count == Configuration.Items.Count;
            var simplifiedItems = Configuration.Items.Select((item, index) =>
            {
                if (!existingMap.TryGetValue(item.Name, out var existing) && canFallbackByIndex)
                {
                    logger.LogInformation("Falling back to index matching for bundle item {Name} at index {Index}", item.Name, index);
                    existing = existingList![index];
                }

                return new SimplifiedBundleItem
                {
                    Name = item.Name,
                    SourceFiles = item.Files.Select(f => f.AbsSourceFile).ToList(),
                    Big = item.IsBig,
                    OutputFormat = existing?.OutputFormat,
                    Compression = existing?.Compression,
                    GenerateMipmaps = existing?.GenerateMipmaps ?? false,
                };
            }).ToList();
            var itemsData = new Dictionary<string, object>
            {
                ["BundleItems"] = simplifiedItems,
            };
            await AtomicWriteFileAsync(itemsPath, System.Text.Json.JsonSerializer.Serialize(itemsData, jsonOptions), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var itemsConfig = new BuildConfiguration { Items = Configuration.Items };
            await AtomicWriteFileAsync(itemsPath, System.Text.Json.JsonSerializer.Serialize(itemsConfig, jsonOptions), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void SyncAlternateConfigDirectory(string projectDir, string configDir, string packsPath, string itemsPath)
    {
        var dirName = Path.GetFileName(configDir);
        var altDirName = dirName.Equals(ModBuilderConstants.LowercaseConfigDir, StringComparison.OrdinalIgnoreCase)
            ? ModBuilderConstants.ConfigDir
            : ModBuilderConstants.LowercaseConfigDir;
        var altDir = Path.Combine(projectDir, altDirName);
        if (!Directory.Exists(altDir))
        {
            return;
        }

        SyncFile(packsPath, Path.Combine(altDir, ModBuilderConstants.BundlePacksConfigFileName), wasSourceJustWritten: true);
        SyncFile(itemsPath, Path.Combine(altDir, ModBuilderConstants.BundleItemsConfigFileName), wasSourceJustWritten: true);
    }

    private static void SyncFile(string sourceFile, string targetFile, bool wasSourceJustWritten = false)
    {
        if (File.Exists(sourceFile) &&
            !string.Equals(targetFile, sourceFile, StringComparison.OrdinalIgnoreCase) &&
            (File.Exists(targetFile) || wasSourceJustWritten))
        {
            File.Copy(sourceFile, targetFile, true);
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
